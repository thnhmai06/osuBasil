using Basil.Server.Features.Irc;
using Basil.Server.Shared.Eventing;
using Basil.Server.Features.Beatmaps;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Shared.Sessions;
using Basil.Server.Features.Chat;
using Basil.Server.Features.Bot;
using Basil.Domain.Multiplayer;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Server.Features.Multiplayer;

/// <summary>Creates, starts, and tears down matches, including the empty-room auto-close watch.</summary>
/// <remarks>
///     Every method here that reads-then-mutates a match's slots or settings must be called with
///     <see cref="MatchSession.Lock" /> already held by the caller; packet handlers own the lock's
///     lifetime, since it must span the eventual state broadcast (see <see cref="MatchSession" />'s
///     doc comment). The one exception is <see cref="CreateAsync" />, which acquires the lock itself
///     around the seat attempt.
/// </remarks>
public sealed class MatchLifecycle(
	IMatchRegistry matchRegistry,
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership,
	ISessionRegistry<GameSession> gameRegistry,
	IMatchRepository matchRepository,
	IMatchRoundEndOutbox roundEndOutbox,
	ILiveEventHub hub,
	IBeatmapRepository beatmapRepo,
	MatchBroadcast broadcast,
	IServiceProvider serviceProvider,
	ILogger<MatchLifecycle> logger)
{
	/// <summary>The outcome of a <see cref="StartAsync" /> attempt.</summary>
	public enum StartOutcome : byte
	{
		Started,
		BeatmapMissing,
		NoOccupiedSlots
	}

	private const int MaxMatchNameLength = 50;

	/* "Match created in 15, invite in 10"
	 * Matches are usually created 15 minutes before start and players are invited
	 * 10 minutes later, so do not close empty rooms too aggressively.
	 */
	private const int EmptyRoomCloseSeconds = 15 * 60;
	private const int EmptyRoomWarnAtSeconds = 5 * 60;

	/// <summary>Validates parsed match-create data against the expected host.</summary>
	/// <param name="data">The parsed match-create data.</param>
	/// <param name="expectedHostId">The host id the data must claim.</param>
	/// <returns>
	///     <see langword="true" /> when the host id matches and the name is short enough; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public static bool ValidateMatchData(MatchState data, int expectedHostId)
	{
		return data.HostId == expectedHostId && data.Name.Length <= MaxMatchNameLength;
	}

	/// <summary>
	///     Creates a match, persists its row, records the creation event, and — when the creator is a
	///     real game client not already seated elsewhere — seats them in slot 0.
	/// </summary>
	/// <remarks>
	///     Sets <see cref="MatchSession.CreatorId" /> to <paramref name="creator" />'s id, granting them
	///     full, permanent <c>!mp</c> authority over this room regardless of referee status (see
	///     <see cref="MatchSession.IsReferee" />/<see cref="MatchSession.IsCreator" />), and joins
	///     <paramref name="creator" /> into the room's own chat channel on every success path — whether
	///     that happens here directly (an unseated creator) or already happened via seating (see
	///     <see cref="MatchMembership" />'s <c>OccupySlot</c>). Every match
	///     starts with <see cref="MatchSession.NoHostId" />; a <see cref="GameSession" />
	///     creator only becomes host once they actually occupy a slot, via the normal
	///     <see cref="MatchMembership.JoinAsync" /> path (there is no special host-bypass case anymore). When the
	///     creator is an <see cref="IrcSession" />, or a <see cref="GameSession" /> already seated in a
	///     different match, nobody is seated in the new room at all — it persists with an empty slot
	///     table under referee control, exactly as if a referee had used <c>!mp make</c> from chat with
	///     no client behind them, and its empty-room auto-close timer starts immediately. Only a
	///     <see cref="MatchMembership.JoinAsync" /> rejection that is not a bare "already seated
	///     elsewhere" outcome (BasilBot creating a room) tears the new room down instead of leaving it
	///     empty — every other join gate (bans, lock, privacy, password, free slots) is unreachable
	///     against a room whose data was just used to create it.
	/// </remarks>
	/// <param name="creator">The userSession creating the room.</param>
	/// <param name="data">The parsed match-create data.</param>
	/// <param name="cancellationToken">A token that cancels the persistence and join operations.</param>
	/// <returns>
	///     The new <see cref="MatchSession" />, or <see langword="null" /> when a game-client creator could not be
	///     seated.
	/// </returns>
	public async Task<MatchSession?> CreateAsync(UserSession creator, MatchState data,
		CancellationToken cancellationToken = default)
	{
		var match = await matchRegistry.CreateAsync(data, MatchSession.NoHostId, cancellationToken);
		match.MutationPublisher = broadcast;
		match.CreatorId = creator.Id;
		logger.LogInformation(
			"+ Match created: MatchId={MatchId} CreatorId={CreatorId} Name={Name}", match.DbId, creator.Id,
			match.Name);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.Created,
			creator.Id, creator.Name, null, null,
			DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		if (creator is GameSession gameCreator)
		{
			// Resolved lazily through the container instead of taking a constructor dependency on
			// MatchMembership: MatchMembership itself depends on this class (for SyncEmptyRoomTimer),
			// so a direct constructor cycle isn't resolvable.
			var membership = serviceProvider.GetRequiredService<MatchMembership>();

			MatchMembership.JoinResult joined;
			await using (var mutation = await match.BeginMutationAsync(cancellationToken))
			{
				joined = await membership.JoinAsync(gameCreator, match, data.Password, cancellationToken);
				// AlreadyInMatch is not a broken room — every other gate (bans, lock, privacy,
				// password, free slots) is unreachable against a room just created with these exact
				// values, so the only way JoinAsync rejects a real game-client creator is if they were
				// already seated elsewhere. That leaves the new room exactly as an IrcSession creator's
				// room starts: no seated players, creator is only a referee. Sync the empty-room timer
				// the same way so the room does not sit orphaned with no auto-close.
				if (joined is MatchMembership.JoinResult.Ok or MatchMembership.JoinResult.AlreadyInMatch)
					SyncEmptyRoomTimer(match);

				// Only JoinResult.Ok actually seated the creator (AlreadyInMatch means nothing changed).
				if (joined is MatchMembership.JoinResult.Ok) mutation.PublishState();
			}

			if (joined is not (MatchMembership.JoinResult.Ok or MatchMembership.JoinResult.AlreadyInMatch))
			{
				await CloseAsync(match, creator.Id, creator.Name, cancellationToken);
				return null;
			}
		}
		else
		{
			await using var mutation = await match.BeginMutationAsync(cancellationToken);
			SyncEmptyRoomTimer(match);
		}

		// The creator isn't necessarily seated (an IrcSession never is; a GameSession creator already
		// seated elsewhere isn't either) — seating already joins the channel via OccupySlot, but nothing
		// else does, so join explicitly here. bypassMatchGate: referee status is granted by the caller
		// right after this method returns, so the participant/referee check would reject this otherwise
		// legitimate join. Idempotent for an already-seated creator (JoinChannel no-ops if already in).
		if (channelRegistry.GetByName(match.ChatChannelName) is { } channel)
			channelMembership.Join(creator, channel, true);

		return match;
	}

	/// <summary>Creates a match with nobody in it, persisting its row and recording the creation event.</summary>
	/// <remarks>
	///     Backs the <c>api.</c> host's <c>POST /match</c>. No chat "sender" exists over HTTP, so there
	///     is no <see cref="UserSession" /> to auto-join into slot 0 the way <see cref="CreateAsync" /> does for
	///     <c>!mp make</c>. <see cref="MatchSession.HostId" /> stays <see cref="MatchSession.NoHostId" />,
	///     <see cref="MatchSession.CreatorId" /> stays null (nobody holds creator authority over this
	///     room), and the referee list stays empty until a caller assigns them via
	///     <c>PATCH /match/{id}/settings</c>, the <c>host</c> action, or the <c>addref</c> action.
	/// </remarks>
	/// <param name="data">The parsed match-create data.</param>
	/// <param name="cancellationToken">A token that cancels the persistence operations.</param>
	/// <returns>The new <see cref="MatchSession" />.</returns>
	public async Task<MatchSession> CreateEmptyAsync(MatchState data,
		CancellationToken cancellationToken = default)
	{
		var match = await matchRegistry.CreateAsync(data, MatchSession.NoHostId, cancellationToken);
		match.MutationPublisher = broadcast;

		logger.LogInformation("+ Match created: MatchId={MatchId} HostId=NoHost Name={Name} (via HTTP)",
			match.DbId, match.Name);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.Created,
			null, null, null, null, DateTimeOffset.UtcNow.UtcDateTime, "Created via HTTP API"), cancellationToken);

		await using (await match.BeginMutationAsync(cancellationToken))
		{
			SyncEmptyRoomTimer(match);
		}

		return match;
	}

	/// <summary>Closes a match: parts every seated userSession, tears the room down, and records a close event.</summary>
	/// <param name="match">The match to close.</param>
	/// <param name="actorId">The acting userSession's id, or <see langword="null" /> for a system action.</param>
	/// <param name="actorName">The acting userSession's name, or <see langword="null" /> when unknown.</param>
	/// <param name="cancellationToken">A token that cancels the close event writes.</param>
	public async Task CloseAsync(MatchSession match, int? actorId = null, string? actorName = null,
		CancellationToken cancellationToken = default)
	{
		var channel = channelRegistry.GetByName(match.ChatChannelName);

		foreach (var slot in match.Slots)
		{
			if (slot.PlayerId is not { } playerId) continue;

			var player = gameRegistry.GetByUserId(playerId);
			if (player is null) continue;

			if (channel is not null) channelMembership.Part(player, channel);
			player.Match = null;
			player.Enqueue(ServerPacketWriter.MatchJoinFail());
		}

		await TeardownMatch(match, cancellationToken);
		logger.LogInformation("- Match closed: MatchId={MatchId} ActorId={ActorId}", match.DbId, actorId);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.Closed,
			actorId, actorName, null, null, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);
	}

	/// <summary>Cancels a pending auto-start countdown when one is queued, announcing why.</summary>
	/// <remarks>
	///     Called whenever a gameplay-affecting setting (map, team type, win condition, size, or a
	///     userSession's team) changes while a <c>!mp start &lt;seconds&gt;</c> countdown is queued, since
	///     starting the match under rules different from what was queued against would be misleading.
	///     A plain <c>!mp timer</c>, which does not start anything on its own, is left alone.
	/// </remarks>
	/// <param name="match">The match whose countdown to cancel.</param>
	public void CancelQueuedAutoStart(MatchSession match)
	{
		if (match.PendingTimer is null || !match.PendingTimerIsAutoStart) return;

		match.PendingTimer.Cancel();
		match.PendingTimer = null;
		match.PendingTimerIsAutoStart = false;

		var bot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
		if (bot is not null)
			broadcast.EnqueueChat(match, bot.Name, bot.Id, "Match start cancelled — room settings changed.");
	}

	/// <summary>Starts the match: validates the beatmap, marks players as playing, creates the round, and broadcasts.</summary>
	/// <param name="match">The match to start.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">A token that cancels the persistence operations.</param>
	/// <returns>
	///     <see cref="StartOutcome.Started" /> when the match started, <see cref="StartOutcome.BeatmapMissing" />
	///     when the assigned beatmap no longer exists on the server (or none was ever assigned), or
	///     <see cref="StartOutcome.NoOccupiedSlots" /> when the room has nobody seated to start with.
	/// </returns>
	public async Task<StartOutcome> StartAsync(MatchSession match, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		// A queued countdown can outlive every player leaving (nothing cancels it just because the
		// room emptied out) and fire with zero occupied slots. Without this guard, InProgress would
		// end up true with every slot Open/Locked, violating the invariant that InProgress implies at
		// least one occupied slot.
		if (match.Slots.All(s => s.PlayerId is null))
		{
			logger.LogDebug("Match start aborted (no players seated): MatchId={MatchId}", match.DbId);
			var emptyBot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
			if (emptyBot is not null)
				broadcast.EnqueueChat(match, emptyBot.Name, emptyBot.Id,
					"Match cannot start because the room has no players.");
			return StartOutcome.NoOccupiedSlots;
		}

		if (match.MapId is not { } mapId)
		{
			// Without this, a match with no beatmap selected starts every occupied slot as Playing
			// anyway. No client can ever send MatchLoadComplete/MatchComplete for a round it has no
			// map for, so the round never finishes, and every later !mp start returns
			// AlreadyInProgress until someone runs !mp abort — see the 2026 investigation's RC4.
			logger.LogDebug("Match start aborted (no beatmap selected): MatchId={MatchId}", match.DbId);
			var noMapBot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
			if (noMapBot is not null)
				broadcast.EnqueueChat(match, noMapBot.Name, noMapBot.Id,
					"Match cannot start because no beatmap has been selected.");
			return StartOutcome.BeatmapMissing;
		}

		var beatmap = await beatmapRepo.FetchOneAsync(mapId, cancellationToken: cancellationToken);
		if (beatmap is null)
		{
			logger.LogDebug("Match start aborted (beatmap missing): MatchId={MatchId} MapId={MapId}",
				match.DbId, match.MapId);
			var bot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
			if (bot is not null)
				broadcast.EnqueueChat(match, bot.Name, bot.Id,
					"Match cannot start because the beatmap does not exist on the server.");
			return StartOutcome.BeatmapMissing;
		}

		var noMap = new List<int>();
		foreach (var slot in match.Slots)
			if (slot.PlayerId is not null)
			{
				if (slot.Status != SlotStatus.NoMap)
					slot.Status = SlotStatus.Playing;
				else
					noMap.Add(slot.PlayerId.Value);
			}

		match.InProgress = true;

		match.CurrentRoundId = await matchRepository.CreateRoundAsync(
			match.DbId, match.NextRoundIndex++, match.MapMd5,
			match.Mode, match.WinCondition, match.TeamType,
			match.Mods, DateTimeOffset.UtcNow.UtcDateTime, cancellationToken);

		broadcast.Enqueue(match, ServerPacketWriter.MatchStart(match.ToPacket()), false, noMap);
		mutation.PublishState();
		logger.LogInformation("~ Match started: MatchId={MatchId} RoundId={RoundId}", match.DbId, match.CurrentRoundId);
		return StartOutcome.Started;
	}

	/// <summary>
	///     Starts, cancels, or leaves alone the empty-room auto-close timer based on whether the match
	///     currently has any occupied slot. Called after every slot-count change (join, leave, and at
	///     creation for a room born with nobody in it).
	/// </summary>
	/// <param name="match">The match whose empty state may have just changed.</param>
	internal void SyncEmptyRoomTimer(MatchSession match)
	{
		var empty = match.Slots.All(s => s.Empty);

		if (!empty)
		{
			if (match.EmptyRoomTimer is null) return;
			match.EmptyRoomTimer.Cancel();
			match.EmptyRoomTimer = null;
			if (match.EmptyRoomWarningSent)
			{
				match.EmptyRoomWarningSent = false;
				broadcast.AnnounceToRoomAndReferees(match,
					"A player joined — the room will no longer be closed for inactivity.");
			}

			return;
		}

		if (match.EmptyRoomTimer is not null) return;

		var cts = new CancellationTokenSource();
		match.EmptyRoomTimer = cts;
		match.EmptyRoomWarningSent = false;
		_ = EmptyRoomCloseLoopAsync(match, cts);
	}

	/// <summary>
	///     Waits out the empty-room grace period, announcing a 60-second warning and then closing the
	///     room if it is still empty, unless canceled first by <see cref="SyncEmptyRoomTimer" />.
	/// </summary>
	private async Task EmptyRoomCloseLoopAsync(MatchSession match, CancellationTokenSource cts)
	{
		using var _ = logger.BeginScope(new Dictionary<string, object> { ["MatchId"] = match.DbId });
		var token = cts.Token;

		// This loop is started fire-and-forget (see SyncEmptyRoomTimer); an exception here would
		// otherwise fault a Task nobody observes, silently losing it instead of reaching the logs.
		try
		{
			if (!await DelayAsync(EmptyRoomCloseSeconds - EmptyRoomWarnAtSeconds, token)) return;

			await using (await match.BeginMutationAsync(token))
			{
				if (token.IsCancellationRequested || !match.Slots.All(s => s.Empty)) return;
				match.EmptyRoomWarningSent = true;
				broadcast.AnnounceToRoomAndReferees(match,
					$"The room is empty and will be closed in {EmptyRoomWarnAtSeconds} seconds unless a player joins.");
			}

			if (!await DelayAsync(EmptyRoomWarnAtSeconds, token)) return;

			await using (await match.BeginMutationAsync(token))
			{
				if (token.IsCancellationRequested || !match.Slots.All(s => s.Empty)) return;
				broadcast.AnnounceToRoomAndReferees(match,
					$"Closing the room — it stayed empty for {EmptyRoomCloseSeconds / 60} minutes.");
				match.EmptyRoomTimer = null;
				match.EmptyRoomWarningSent = false;
				await CloseAsync(match, null, null, token);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogError(ex, "Empty-room close loop failed: MatchId={MatchId}", match.DbId);
		}
	}

	/// <summary>Delays for the given number of seconds, returning whether it completed uncancelled.</summary>
	private static async Task<bool> DelayAsync(int seconds, CancellationToken token)
	{
		if (seconds <= 0) return !token.IsCancellationRequested;

		try
		{
			await Task.Delay(TimeSpan.FromSeconds(seconds), token);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}

	/// <summary>
	///     Cancels the match's timers, waits for any round-end write still queued for it to finish
	///     persisting (or give up), ends every live SSE subscriber still connected to it, then
	///     removes it from the registry and notifies the lobby.
	/// </summary>
	/// <remarks>
	///     The drain (ADR-003) guarantees the last round's end is never discarded along with the
	///     match's in-memory state — a match with no pending write returns immediately. The SSE
	///     completion (ADR-004) guarantees a client still connected when the match closes observes
	///     end-of-stream right away instead of its handler staying attached indefinitely.
	/// </remarks>
	private async Task TeardownMatch(MatchSession match, CancellationToken cancellationToken)
	{
		match.PendingTimer?.Cancel();
		match.PendingTimer = null;
		match.EmptyRoomTimer?.Cancel();
		match.EmptyRoomTimer = null;

		await roundEndOutbox.DrainAsync(match.DbId, cancellationToken);

		// Ends every subscriber still connected to this match's live SSE streams (ADR-004) — without
		// this, a client connected when the match closes would keep its handler attached to the
		// live-event hub indefinitely (or until it happens to disconnect on its own).
		match.SseSubscribers.CompleteAll();
		hub.Forget(MatchStreams.Category, match.DbId);

		matchRegistry.Remove(match.Id);

		var lobby = channelRegistry.GetByName("#lobby");
		if (lobby is not null) channelMembership.BroadcastToMembers(lobby, ServerPacketWriter.DisposeMatch(match.Id));
	}
}