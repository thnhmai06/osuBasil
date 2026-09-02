using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Backgrounds;
using Basil.Application.Diagnostics;
using Basil.Application.Services.Bot;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Provides the shared multiplayer membership operations: room creation, join, leave, close, and
///     state broadcasts.
/// </summary>
/// <remarks>
///     Every method here that reads-then-mutates a match's slots or settings must be called with
///     <see cref="MatchSession.Lock" /> already held by the caller; packet handlers own the lock's
///     lifetime, since it must span the eventual state broadcast (see <see cref="MatchSession" />'s
///     doc comment). The one exception is <see cref="CreateAsync" />, which acquires the lock itself
///     around the seat attempt.
/// </remarks>
public sealed class MatchMembershipService(
	IMatchRegistry matchRegistry,
	IChannelRegistry channelRegistry,
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	ChannelMembershipService channelMembership,
	IMatchRepository matchRepository,
	IMatchRoundEndOutbox roundEndOutbox,
	IMatchLiveEvents eventBus,
	IBeatmapRepository beatmapRepo,
	IUserRepository userRepo,
	ILogger<MatchMembershipService> logger)
{
	/// <summary>The outcome of a <see cref="JoinAsync" /> attempt.</summary>
	public enum JoinResult : byte
	{
		Ok,
		AlreadyInMatch,
		TourneyClient,
		Banned,
		Locked,
		Private,
		WrongPassword,
		NoFreeSlot,
		BotCannotSeat
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
	///     <see cref="OccupySlot" />). Every match
	///     starts with <see cref="MatchSession.NoHostId" />; a <see cref="GameSession" />
	///     creator only becomes host once they actually occupy a slot, via the normal
	///     <see cref="JoinAsync" /> path (there is no special host-bypass case anymore). When the
	///     creator is an <see cref="IrcSession" />, or a <see cref="GameSession" /> already seated in a
	///     different match, nobody is seated in the new room at all — it persists with an empty slot
	///     table under referee control, exactly as if a referee had used <c>!mp make</c> from chat with
	///     no client behind them, and its empty-room auto-close timer starts immediately. Only a
	///     <see cref="MatchMembershipService.JoinAsync" /> rejection that is not a bare "already seated
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
			JoinResult joined;
			await match.Lock.WaitAsync(cancellationToken);
			try
			{
				joined = await JoinAsync(gameCreator, match, data.Password, cancellationToken);
				// AlreadyInMatch is not a broken room — every other gate (bans, lock, privacy,
				// password, free slots) is unreachable against a room just created with these exact
				// values, so the only way JoinAsync rejects a real game-client creator is if they were
				// already seated elsewhere. That leaves the new room exactly as an IrcSession creator's
				// room starts: no seated players, creator is only a referee. Sync the empty-room timer
				// the same way so the room does not sit orphaned with no auto-close.
				if (joined is JoinResult.Ok or JoinResult.AlreadyInMatch) SyncEmptyRoomTimer(match);
			}
			finally
			{
				match.Lock.Release();
			}

			if (joined is not (JoinResult.Ok or JoinResult.AlreadyInMatch))
			{
				await CloseAsync(match, creator.Id, creator.Name, cancellationToken);
				return null;
			}

			// Only JoinResult.Ok actually seated the creator (AlreadyInMatch means nothing changed).
			if (joined is JoinResult.Ok)
				await EnqueueStateAsync(match, match.NextStateVersion(), cancellationToken: cancellationToken);
		}
		else
		{
			await match.Lock.WaitAsync(cancellationToken);
			try
			{
				SyncEmptyRoomTimer(match);
			}
			finally
			{
				match.Lock.Release();
			}
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

		logger.LogInformation("+ Match created: MatchId={MatchId} HostId=NoHost Name={Name} (via HTTP)",
			match.DbId, match.Name);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.Created,
			null, null, null, null, DateTimeOffset.UtcNow.UtcDateTime, "Created via HTTP API"), cancellationToken);

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			SyncEmptyRoomTimer(match);
		}
		finally
		{
			match.Lock.Release();
		}

		return match;
	}

	/// <summary>Seats a <see cref="GameSession" /> in a match after applying every join gate.</summary>
	/// <remarks>
	///     Rejects the join, sending a <c>MatchJoinFail</c> packet, when the userSession is already in a
	///     match, is a tourney client, is banned, or the room is locked; when the room is private and
	///     the userSession holds no staff privileges or invite; when the password is wrong; or when no free
	///     slot exists. BasilBot itself is never seatable.
	/// </remarks>
	/// <param name="userSession">The userSession joining.</param>
	/// <param name="match">The match to join.</param>
	/// <param name="password">The password supplied by the userSession.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	/// <returns>The outcome of the join attempt.</returns>
	public async Task<JoinResult> JoinAsync(
		GameSession userSession, MatchSession match, string password,
		CancellationToken cancellationToken = default)
	{
		JoinResult? rejection = userSession.IsBot ? JoinResult.BotCannotSeat
			: userSession.Match is not null ? JoinResult.AlreadyInMatch
			: match.TourneyClients.Contains(userSession.Id) ? JoinResult.TourneyClient
			: match.BannedIds.Contains(userSession.Id) ? JoinResult.Banned
			: match.IsLocked ? JoinResult.Locked
			: null;

		if (rejection is { } reason)
		{
			logger.LogDebug("Join rejected: MatchId={MatchId} UserId={UserId} Reason={Reason}",
				match.DbId, userSession.Id, reason);
			userSession.Enqueue(ServerPacketWriter.MatchJoinFail());
			return reason;
		}

		if (match.IsPrivate && (userSession.Privilege & UserPrivileges.Staff) == 0 &&
		    !match.InvitedIds.Contains(userSession.Id))
		{
			logger.LogDebug("Join rejected: MatchId={MatchId} UserId={UserId} Reason=Private", match.DbId,
				userSession.Id);
			userSession.Enqueue(ServerPacketWriter.MatchJoinFail());
			return JoinResult.Private;
		}

		if (password != match.Password && (userSession.Privilege & UserPrivileges.Staff) == 0)
		{
			logger.LogDebug("Join rejected: MatchId={MatchId} UserId={UserId} Reason=WrongPassword",
				match.DbId, userSession.Id);
			userSession.Enqueue(ServerPacketWriter.MatchJoinFail());
			return JoinResult.WrongPassword;
		}

		var free = match.GetFreeSlotId();
		if (free is null)
		{
			logger.LogDebug("Join rejected: MatchId={MatchId} UserId={UserId} Reason=Full", match.DbId,
				userSession.Id);
			userSession.Enqueue(ServerPacketWriter.MatchJoinFail());
			return JoinResult.NoFreeSlot;
		}

		return await OccupySlot(userSession, match, free.Value, cancellationToken)
			? JoinResult.Ok
			: JoinResult.NoFreeSlot;
	}

	/// <summary>Seats a userSession directly, bypassing every join gate.</summary>
	/// <remarks>
	///     Server-initiated seating for a force-invite (<see cref="MatchControlService.ForceInviteAsync" />).
	///     Password, private, locked, and ban gates are not re-checked here; the caller already did.
	///     Fails only when the userSession is already in a match, the room is full, or the userSession is BasilBot.
	/// </remarks>
	/// <param name="userSession">The userSession to seat.</param>
	/// <param name="match">The match to seat into.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	/// <returns>The outcome of the join attempt.</returns>
	public async Task<JoinResult> ForceJoinAsync(GameSession userSession, MatchSession match,
		CancellationToken cancellationToken = default)
	{
		if (userSession.IsBot) return JoinResult.BotCannotSeat;
		if (userSession.Match is not null) return JoinResult.AlreadyInMatch;

		var free = match.GetFreeSlotId();
		if (free is null) return JoinResult.NoFreeSlot;

		return await OccupySlot(userSession, match, free.Value, cancellationToken)
			? JoinResult.Ok
			: JoinResult.NoFreeSlot;
	}

	/// <summary>Occupies a specific slot, runs the shared join tail, and broadcasts the new state.</summary>
	/// <remarks>
	///     Shared by <see cref="JoinAsync" /> and <see cref="ForceJoinAsync" />. The tail covers the
	///     channel join, the team default, the slot fields, the gameplay-host auto-assign for a room
	///     that had nobody in it, the <c>MatchJoinSuccess</c> packet, the state broadcast, and the
	///     <c>PlayerJoined</c> event. Throws if <paramref name="userSession" /> is BasilBot: both public
	///     callers already reject that case, so reaching this point with a bot session is a
	///     programming error, not a normal rejection.
	/// </remarks>
	/// <param name="userSession">The userSession being seated.</param>
	/// <param name="match">The match being joined.</param>
	/// <param name="slotId">The 0-based slot index to occupy.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	/// <returns>
	///     <see langword="true" /> when the userSession was seated; otherwise, <see langword="false" /> when the channel is
	///     missing or the channel join failed.
	/// </returns>
	private async Task<bool> OccupySlot(GameSession userSession, MatchSession match, int slotId,
		CancellationToken cancellationToken = default)
	{
		if (userSession.IsBot)
			throw new InvalidOperationException("System-owned game sessions cannot occupy multiplayer slots.");

		var channel = channelRegistry.GetByName(match.ChatChannelName);
		// bypassMatchGate: the slot isn't assigned until below, so the participant check would reject
		// this legitimate seat — every prior gate in JoinAsync/ForceJoinAsync already authorized it.
		if (channel is null || !channelMembership.Join(userSession, channel, true)) return false;

		var lobby = channelRegistry.GetByName("#lobby");
		if (lobby is not null && userSession.InChannel(lobby.Name)) channelMembership.Part(userSession, lobby);

		var slot = match.Slots[slotId];
		if (match.TeamType is MatchTeamType.TeamVs or MatchTeamType.TagTeamVs)
		{
			var counts = match.Slots
				.Where(s => s.PlayerId is not null)
				.GroupBy(s => s.Team)
				.ToDictionary(g => g.Key, g => g.Count());
			counts.TryGetValue(MatchTeam.Red, out var redCount);
			counts.TryGetValue(MatchTeam.Blue, out var blueCount);
			slot.Team = redCount <= blueCount ? MatchTeam.Red : MatchTeam.Blue;
		}

		slot.Status = SlotStatus.NotReady;
		slot.PlayerId = userSession.Id;
		userSession.Match = match;

		if (!match.HasGameplayHost) match.HostId = userSession.Id;

		userSession.Enqueue(ServerPacketWriter.MatchJoinSuccess(match.ToPacket()));
		// SyncEmptyRoomTimer still needs the caller's lock held. The state publish itself does not
		// (ADR-004 4b follow-up) -- the caller allocates a version and publishes after releasing the
		// lock instead, since a version allocated an instant later than the mutation is still correct.
		SyncEmptyRoomTimer(match);

		logger.LogInformation("+ User joined match: MatchId={MatchId} UserId={UserId} SlotId={SlotId}",
			match.DbId, userSession.Id, slotId);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.PlayerJoined,
			userSession.Id, userSession.Name, null,
			null, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		return true;
	}

	/// <summary>Parts a userSession from a match, transferring the host when needed.</summary>
	/// <remarks>
	///     No longer tears the room down when every slot empties — an empty room persists under
	///     referee control (exactly like an IRC-created one) until <see cref="SyncEmptyRoomTimer" />'s
	///     5-minute auto-close fires or a referee runs <c>!mp close</c>.
	/// </remarks>
	/// <param name="userSession">The userSession leaving.</param>
	/// <param name="match">The match being left.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	public async Task LeaveAsync(GameSession userSession, MatchSession match,
		CancellationToken cancellationToken = default)
	{
		var slot = match.GetSlot(userSession.Id);
		if (slot is null)
		{
			userSession.Match = null;
			return;
		}

		slot.Reset(slot.Status == SlotStatus.Locked ? SlotStatus.Locked : SlotStatus.Open);

		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is not null) channelMembership.Part(userSession, channel);

		var hostTransfer = false;
		int? prevHostId = null;
		int? newHostId = null;

		if (userSession.Id == match.HostId)
		{
			prevHostId = match.HostId;
			var newHostSlot = match.Slots.FirstOrDefault(s => !s.Empty);
			if (newHostSlot is not null)
			{
				newHostId = newHostSlot.PlayerId!.Value;
				match.HostId = newHostId.Value;
				hostTransfer = true;
				gameRegistry.GetByUserId(match.HostId)?.Enqueue(ServerPacketWriter.MatchTransferHost());
			}
			else
			{
				match.HostId = MatchSession.NoHostId;
			}
		}

		// SyncEmptyRoomTimer still needs the caller's lock held. The state publish itself does not
		// (ADR-004 4b follow-up) -- the caller allocates a version and publishes after releasing the
		// lock instead, since a version allocated an instant later than the mutation is still correct.
		SyncEmptyRoomTimer(match);

		userSession.Match = null;

		logger.LogInformation("- User left match: MatchId={MatchId} UserId={UserId}", match.DbId, userSession.Id);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.PlayerLeft,
			userSession.Id, userSession.Name, null,
			null, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		if (hostTransfer)
		{
			logger.LogInformation(
				"Host transferred on leave: MatchId={MatchId} PrevHostId={PrevHostId} NewHostId={NewHostId}",
				match.DbId, prevHostId, newHostId);

			var prevHostName = prevHostId is not null
				? gameRegistry.GetByUserId(prevHostId.Value)?.Name
				: null;
			var newHostName = newHostId is not null
				? gameRegistry.GetByUserId(newHostId.Value)?.Name
				: null;
			await matchRepository.CreateEventAsync(new MatchEvent(
				match.DbId, (int)MatchEventType.HostGranted,
				prevHostId, prevHostName, newHostId, newHostName,
				DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);
		}
	}

	/// <summary>
	///     Joins a non-seated participant (an IRC-only referee, or a userSession granted access via
	///     <c>!mp join</c>) into the match's chat channel, without touching any slot.
	/// </summary>
	/// <param name="session">The session joining the match's chat.</param>
	/// <param name="match">The match whose chat is being joined.</param>
	/// <param name="bypassMatchGate">
	///     When <see langword="true" />, skips the participant/referee authorization check — reserved
	///     for a caller that already authorized the join through its own gates. See
	///     <see cref="Sessions.Channels.ChannelMembershipService.Join" /> for what this bypasses.
	/// </param>
	/// <returns><see langword="true" /> if the session was added to the channel; otherwise, <see langword="false" />.</returns>
	public bool JoinMatchChat(UserSession session, MatchSession match, bool bypassMatchGate = false)
	{
		return channelRegistry.GetByName(match.ChatChannelName) is { } channel &&
		       channelMembership.Join(session, channel, bypassMatchGate);
	}

	/// <summary>
	///     Syncs a match's chat channel topic to the match's current <see cref="MatchSession.Name" />.
	/// </summary>
	/// <remarks>
	///     Called after every room rename (<c>!mp name</c>, the equivalent HTTP route, and the native
	///     client's settings-sync packet) so the channel's topic never drifts from the room name shown
	///     everywhere else. A no-op when the topic already matches (see
	///     <see cref="Sessions.Channels.ChannelMembershipService.SyncTopic" />).
	/// </remarks>
	/// <param name="match">The match whose channel topic to sync.</param>
	public void SyncChannelTopic(MatchSession match)
	{
		if (channelRegistry.GetByName(match.ChatChannelName) is { } channel)
			channelMembership.SyncTopic(channel, match.Name);
	}

	/// <summary>
	///     Parts a non-seated participant (an IRC-only referee, or a session merely scoped/present in
	///     the match's chat channel) from the match's chat, without touching any slot.
	/// </summary>
	/// <param name="session">The session leaving the match's chat.</param>
	/// <param name="match">The match whose chat is being left.</param>
	public void LeaveMatchChat(UserSession session, MatchSession match)
	{
		if (channelRegistry.GetByName(match.ChatChannelName) is { } channel)
			channelMembership.Part(session, channel, false);
		if (session.MpScopeMatchId == match.DbId) session.MpScopeMatchId = null;
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
			EnqueueChat(match, bot.Name, bot.Id, "Match start cancelled — room settings changed.");
	}

	/// <summary>Starts the match: validates the beatmap, marks players as playing, creates the round, and broadcasts.</summary>
	/// <param name="match">The match to start.</param>
	/// <param name="cancellationToken">A token that cancels the persistence and broadcast operations.</param>
	/// <returns>
	///     <see langword="true" /> when the match started; otherwise, <see langword="false" /> when the assigned beatmap
	///     no longer exists on the server.
	/// </returns>
	public async Task<bool> StartAsync(MatchSession match, CancellationToken cancellationToken = default)
	{
		if (match.MapId is not { } mapId)
		{
			// Without this, a match with no beatmap selected starts every occupied slot as Playing
			// anyway. No client can ever send MatchLoadComplete/MatchComplete for a round it has no
			// map for, so the round never finishes, and every later !mp start returns
			// AlreadyInProgress until someone runs !mp abort — see the 2026 investigation's RC4.
			logger.LogDebug("Match start aborted (no beatmap selected): MatchId={MatchId}", match.DbId);
			var noMapBot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
			if (noMapBot is not null)
				EnqueueChat(match, noMapBot.Name, noMapBot.Id,
					"Match cannot start because no beatmap has been selected.");
			return false;
		}

		var beatmap = await beatmapRepo.FetchOneAsync(mapId, cancellationToken: cancellationToken);
		if (beatmap is null)
		{
			logger.LogDebug("Match start aborted (beatmap missing): MatchId={MatchId} MapId={MapId}",
				match.DbId, match.MapId);
			var bot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
			if (bot is not null)
				EnqueueChat(match, bot.Name, bot.Id,
					"Match cannot start because the beatmap does not exist on the server.");
			return false;
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

		Enqueue(match, ServerPacketWriter.MatchStart(match.ToPacket()), false, noMap);
		// Not hoisted (ADR-004 4b): the round-start write above already holds the lock through a DB
		// await per ADR-003's decision to keep round-start synchronous, so shrinking just this tail
		// call's lock scope would not meaningfully reduce hold time.
		await EnqueueStateAsync(match, match.NextStateVersion(), cancellationToken: cancellationToken);
		logger.LogInformation("~ Match started: MatchId={MatchId} RoundId={RoundId}", match.DbId, match.CurrentRoundId);
		return true;
	}

	/// <summary>Broadcasts a raw packet to the match channel and, for public rooms, the non-empty lobby.</summary>
	/// <param name="match">The match whose channel to broadcast into.</param>
	/// <param name="data">The serialized packet bytes.</param>
	/// <param name="lobby"><see langword="true" /> to also broadcast to the lobby; otherwise, <see langword="false" />.</param>
	/// <param name="immune">User ids to exclude from the broadcast, or <see langword="null" /> for none.</param>
	public void Enqueue(MatchSession match, byte[] data, bool lobby = true, IReadOnlyCollection<int>? immune = null)
	{
		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is not null) channelMembership.BroadcastToMembers(channel, data, immune);

		if (!match.IsPrivate) BroadcastToNonEmptyLobby(data, lobby);
	}

	/// <summary>Broadcasts a chat message into the match channel.</summary>
	/// <param name="match">The match whose channel to broadcast into.</param>
	/// <param name="senderName">The message sender's name.</param>
	/// <param name="senderId">The message sender's id.</param>
	/// <param name="text">The message text.</param>
	public void EnqueueChat(MatchSession match, string senderName, int senderId, string text)
	{
		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is null) return;

		channelMembership.BroadcastPrivmsg(channel,
			IrcMessageWriter.Privmsg(senderName, senderId, channel.Name, text));
	}

	private static readonly KeyValuePair<string, object?> PacketStreamTag = new("stream", "packet");

	/// <summary>Broadcasts the match state to the channel and lobby and republishes every live SSE snapshot channel.</summary>
	/// <remarks>
	///     Sends the <c>UpdateMatch</c> packet to the match channel and, for public rooms, the lobby,
	///     then rebuilds and publishes the main, settings, per-slot, and whole-arrangement slots
	///     snapshots through <see cref="IMatchLiveEvents" />. This is the single call path every
	///     slot-mutating operation (packet-driven or HTTP-driven) routes through, so <c>slot</c> and
	///     <c>slots</c> always fire together (ADR-004) — no separate path publishes one without the
	///     other. A channel whose <see cref="SnapshotChannel{T}.Publish" /> found nothing changed is
	///     skipped rather than emitting a no-op patch.
	/// </remarks>
	/// <remarks>
	///     Runs entirely without holding <see cref="MatchSession.Lock" /> (ADR-004 4b) — every build
	///     and broadcast here is gated by <paramref name="version" /> instead, so a call superseded
	///     by a newer mutation's is dropped rather than reverting live state to something stale. The
	///     caller must allocate <paramref name="version" /> from <see cref="MatchSession.NextStateVersion" />
	///     after the mutation it is reporting has completed; the lock does not need to still be held
	///     at that point; allocating it slightly later can only produce a newer version, never a stale
	///     one.
	/// </remarks>
	/// <param name="match">The match whose state to broadcast.</param>
	/// <param name="version">This call's state version, from <see cref="MatchSession.NextStateVersion" />.</param>
	/// <param name="lobby"><see langword="true" /> to also broadcast to the lobby; otherwise, <see langword="false" />.</param>
	/// <param name="cancellationToken">A token that cancels the snapshot builds.</param>
	public async Task EnqueueStateAsync(MatchSession match, long version, bool lobby = true,
		CancellationToken cancellationToken = default)
	{
		if (match.PacketBroadcastGate.TryAdvance(version))
		{
			var channel = channelRegistry.GetByName(match.ChatChannelName);
			if (channel is not null)
				channelMembership.BroadcastToMembers(channel,
					ServerPacketWriter.UpdateMatch(match.ToPacket()));

			if (!match.IsPrivate)
				BroadcastToNonEmptyLobby(ServerPacketWriter.UpdateMatch(match.ToPacket(), false), lobby);
		}
		else
		{
			BasilMetrics.StalePublishDropped.Add(1, PacketStreamTag);
		}

		var mainSnapshot = await MatchLiveSnapshotBuilder.BuildMain(
			match, gameRegistry, ircRegistry, userRepo, beatmapRepo, cancellationToken);
		if (match.MainSnapshot.Publish(mainSnapshot, version) is { } mainDelta)
			eventBus.PublishMain(match.DbId, mainDelta);

		var settings = await MatchLiveSnapshotBuilder.BuildSettings(
			match, gameRegistry, ircRegistry, userRepo, beatmapRepo, cancellationToken);
		if (match.SettingsSnapshot.Publish(settings, version) is { } settingsDelta)
			eventBus.PublishSettings(match.DbId, settingsDelta);

		for (var i = 0; i < match.SlotSnapshots.Count; i++)
			if (match.SlotSnapshots[i].Publish(mainSnapshot.Slots[i], version) is { } slotDelta)
				eventBus.PublishSlot(match.DbId, i, slotDelta);

		// Reuses mainSnapshot.Slots (already resolved above) instead of a second occupant-lookup
		// pass — MatchSlotsView wraps the exact same per-slot view list BuildSlots itself produces.
		if (match.SlotsSnapshot.Publish(new MatchSlotsView(mainSnapshot.Slots), version) is { } slotsDelta)
			eventBus.PublishSlots(match.DbId, slotsDelta);
	}

	/// <summary>Rebuilds and republishes the host snapshot channel.</summary>
	/// <remarks>Runs without holding <see cref="MatchSession.Lock" />, gated by <paramref name="version" /> (ADR-004 4b).</remarks>
	/// <param name="match">The match whose host to publish.</param>
	/// <param name="version">This call's state version, from <see cref="MatchSession.NextStateVersion" />.</param>
	/// <param name="cancellationToken">A token that cancels the host lookup.</param>
	public async Task PublishHostAsync(MatchSession match, long version, CancellationToken cancellationToken = default)
	{
		var host = await MatchLiveSnapshotBuilder.BuildHost(match, gameRegistry, ircRegistry, userRepo,
			cancellationToken);
		if (match.HostSnapshot.Publish(host, version) is { } delta)
			eventBus.PublishHost(match.DbId, delta);
	}

	/// <summary>Rebuilds and republishes the referee list snapshot channel.</summary>
	/// <remarks>Runs without holding <see cref="MatchSession.Lock" />, gated by <paramref name="version" /> (ADR-004 4b).</remarks>
	/// <param name="match">The match whose referees to publish.</param>
	/// <param name="version">This call's state version, from <see cref="MatchSession.NextStateVersion" />.</param>
	/// <param name="cancellationToken">A token that cancels the referee lookups.</param>
	public async Task PublishRefsAsync(MatchSession match, long version, CancellationToken cancellationToken = default)
	{
		var refs = await MatchLiveSnapshotBuilder.BuildRefs(
			match, gameRegistry, ircRegistry, userRepo, cancellationToken);
		if (match.RefsSnapshot.Publish(refs, version) is { } delta)
			eventBus.PublishRefs(match.DbId, delta);
	}

	/// <summary>Rebuilds and republishes the banlist snapshot channel.</summary>
	/// <remarks>Runs without holding <see cref="MatchSession.Lock" />, gated by <paramref name="version" /> (ADR-004 4b).</remarks>
	/// <param name="match">The match whose banlist to publish.</param>
	/// <param name="version">This call's state version, from <see cref="MatchSession.NextStateVersion" />.</param>
	/// <param name="cancellationToken">A token that cancels the ban lookups.</param>
	public async Task PublishBansAsync(MatchSession match, long version, CancellationToken cancellationToken = default)
	{
		var bans = await MatchLiveSnapshotBuilder.BuildBans(match, gameRegistry, ircRegistry, userRepo,
			cancellationToken);
		if (match.BansSnapshot.Publish(bans, version) is { } delta)
			eventBus.PublishBans(match.DbId, delta);
	}

	/// <summary>Republishes the countdown timer snapshot channel.</summary>
	/// <param name="match">The match whose timer to publish.</param>
	/// <param name="version">This call's state version, from <see cref="MatchSession.NextStateVersion" />.</param>
	public void PublishTimer(MatchSession match, long version)
	{
		if (match.TimerSnapshot.Publish(MatchLiveSnapshotBuilder.BuildTimer(match), version) is { } delta)
			eventBus.PublishTimer(match.DbId, delta);
	}

	/// <summary>
	///     Starts, cancels, or leaves alone the empty-room auto-close timer based on whether the match
	///     currently has any occupied slot. Called after every slot-count change (join, leave, and at
	///     creation for a room born with nobody in it).
	/// </summary>
	/// <param name="match">The match whose empty state may have just changed.</param>
	private void SyncEmptyRoomTimer(MatchSession match)
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
				AnnounceToRoomAndReferees(match, "A player joined — the room will no longer be closed for inactivity.");
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

			await match.Lock.WaitAsync(token);
			try
			{
				if (token.IsCancellationRequested || !match.Slots.All(s => s.Empty)) return;
				match.EmptyRoomWarningSent = true;
				AnnounceToRoomAndReferees(match,
					$"The room is empty and will be closed in {EmptyRoomWarnAtSeconds} seconds unless a player joins.");
			}
			finally
			{
				match.Lock.Release();
			}

			if (!await DelayAsync(EmptyRoomWarnAtSeconds, token)) return;

			await match.Lock.WaitAsync(token);
			try
			{
				if (token.IsCancellationRequested || !match.Slots.All(s => s.Empty)) return;
				AnnounceToRoomAndReferees(match,
					$"Closing the room — it stayed empty for {EmptyRoomCloseSeconds / 60} minutes.");
				match.EmptyRoomTimer = null;
				match.EmptyRoomWarningSent = false;
				await CloseAsync(match, null, null, token);
			}
			finally
			{
				match.Lock.Release();
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
	///     Announces a message into the match's chat channel and, for any referee not already reached
	///     through that channel, as a direct message.
	/// </summary>
	internal void AnnounceToRoomAndReferees(MatchSession match, string text)
	{
		var bot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
		if (bot is null) return;

		EnqueueChat(match, bot.Name, bot.Id, text);

		var channel = channelRegistry.GetByName(match.ChatChannelName);
		foreach (var refereeId in match.Referees)
		{
			if (channel is not null && channel.Contains(refereeId)) continue;
			if (gameRegistry.GetByUserId(refereeId) is { } game)
				game.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, game.Name, text));
			if (ircRegistry.GetByUserId(refereeId) is { } irc)
				irc.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, irc.Name, text));
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
		eventBus.Forget(match.DbId);

		matchRegistry.Remove(match.Id);

		var lobby = channelRegistry.GetByName("#lobby");
		if (lobby is not null) channelMembership.BroadcastToMembers(lobby, ServerPacketWriter.DisposeMatch(match.Id));
	}

	/// <summary>Broadcasts a packet to the lobby channel, but only when it is non-empty.</summary>
	/// <param name="data">The serialized packet bytes.</param>
	/// <param name="lobby"><see langword="true" /> to allow the lobby broadcast; otherwise, <see langword="false" />.</param>
	private void BroadcastToNonEmptyLobby(byte[] data, bool lobby)
	{
		if (!lobby) return;

		var lobbyChannel = channelRegistry.GetByName("#lobby");
		if (lobbyChannel is not null && lobbyChannel.PlayerCount > 0)
			channelMembership.BroadcastToMembers(lobbyChannel, data);
	}
}