using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
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
		await EnqueueStateAsync(match, cancellationToken: cancellationToken);
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

		await EnqueueStateAsync(match, cancellationToken: cancellationToken);
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

		TeardownMatch(match);
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
		var beatmap = match.MapId > 0
			? await beatmapRepo.FetchOneAsync(match.MapId, cancellationToken: cancellationToken)
			: null;

		if (match.MapId > 0 && beatmap is null)
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
		await EnqueueStateAsync(match, cancellationToken: cancellationToken);
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

	/// <summary>Broadcasts the match state to the channel and lobby and republishes every live SSE snapshot channel.</summary>
	/// <remarks>
	///     Sends the <c>UpdateMatch</c> packet to the match channel and, for public rooms, the lobby,
	///     then rebuilds and publishes the main, settings, and per-slot snapshots through
	///     <see cref="IMatchLiveEvents" />.
	/// </remarks>
	/// <param name="match">The match whose state to broadcast.</param>
	/// <param name="lobby"><see langword="true" /> to also broadcast to the lobby; otherwise, <see langword="false" />.</param>
	/// <param name="cancellationToken">A token that cancels the snapshot builds.</param>
	public async Task EnqueueStateAsync(MatchSession match, bool lobby = true,
		CancellationToken cancellationToken = default)
	{
		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is not null)
			channelMembership.BroadcastToMembers(channel,
				ServerPacketWriter.UpdateMatch(match.ToPacket()));

		if (!match.IsPrivate)
			BroadcastToNonEmptyLobby(ServerPacketWriter.UpdateMatch(match.ToPacket(), false), lobby);

		var mainSnapshot = await MatchLiveSnapshotBuilder.BuildMain(
			match, gameRegistry, ircRegistry, userRepo, beatmapRepo, cancellationToken);
		eventBus.PublishMain(match.DbId, match.MainSnapshot.Publish(mainSnapshot));

		var settings = await MatchLiveSnapshotBuilder.BuildSettings(
			match, gameRegistry, ircRegistry, userRepo, beatmapRepo, cancellationToken);
		var settingsDelta = match.SettingsSnapshot.Publish(settings);
		eventBus.PublishSettings(match.DbId, settingsDelta);

		for (var i = 0; i < match.SlotSnapshots.Count; i++)
		{
			var slotDelta = match.SlotSnapshots[i].Publish(mainSnapshot.Slots[i]);
			eventBus.PublishSlot(match.DbId, i, slotDelta);
		}
	}

	/// <summary>Rebuilds and republishes the host snapshot channel.</summary>
	/// <param name="match">The match whose host to publish.</param>
	/// <param name="cancellationToken">A token that cancels the host lookup.</param>
	public async Task PublishHostAsync(MatchSession match, CancellationToken cancellationToken = default)
	{
		var host = await MatchLiveSnapshotBuilder.BuildHost(match, gameRegistry, ircRegistry, userRepo,
			cancellationToken);
		var delta = match.HostSnapshot.Publish(host);
		eventBus.PublishHost(match.DbId, delta);
	}

	/// <summary>Rebuilds and republishes the referee list snapshot channel.</summary>
	/// <param name="match">The match whose referees to publish.</param>
	/// <param name="cancellationToken">A token that cancels the referee lookups.</param>
	public async Task PublishRefsAsync(MatchSession match, CancellationToken cancellationToken = default)
	{
		var refs = await MatchLiveSnapshotBuilder.BuildRefs(
			match, gameRegistry, ircRegistry, userRepo, cancellationToken);
		var delta = match.RefsSnapshot.Publish(refs);
		eventBus.PublishRefs(match.DbId, delta);
	}

	/// <summary>Rebuilds and republishes the banlist snapshot channel.</summary>
	/// <param name="match">The match whose banlist to publish.</param>
	/// <param name="cancellationToken">A token that cancels the ban lookups.</param>
	public async Task PublishBansAsync(MatchSession match, CancellationToken cancellationToken = default)
	{
		var bans = await MatchLiveSnapshotBuilder.BuildBans(match, gameRegistry, ircRegistry, userRepo,
			cancellationToken);
		var delta = match.BansSnapshot.Publish(bans);
		eventBus.PublishBans(match.DbId, delta);
	}

	/// <summary>Republishes the countdown timer snapshot channel.</summary>
	/// <param name="match">The match whose timer to publish.</param>
	public void PublishTimer(MatchSession match)
	{
		var delta = match.TimerSnapshot.Publish(MatchLiveSnapshotBuilder.BuildTimer(match));
		eventBus.PublishTimer(match.DbId, delta);
	}

	/// <summary>Rebuilds and republishes the slots' snapshot channel.</summary>
	/// <param name="match">The match whose slots to publish.</param>
	/// <param name="cancellationToken">A token that cancels the occupant lookups.</param>
	public async Task PublishSlotsAsync(MatchSession match, CancellationToken cancellationToken = default)
	{
		var slots = await MatchLiveSnapshotBuilder.BuildSlots(match, gameRegistry, ircRegistry, userRepo,
			cancellationToken);
		var delta = match.SlotsSnapshot.Publish(slots);
		eventBus.PublishSlots(match.DbId, delta);
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
			AnnounceToRoomAndReferees(match, "Closing the room — it stayed empty for 5 minutes.");
			match.EmptyRoomTimer = null;
			match.EmptyRoomWarningSent = false;
			await CloseAsync(match, null, null, token);
		}
		finally
		{
			match.Lock.Release();
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
	private void AnnounceToRoomAndReferees(MatchSession match, string text)
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

	private void TeardownMatch(MatchSession match)
	{
		match.PendingTimer?.Cancel();
		match.PendingTimer = null;
		match.EmptyRoomTimer?.Cancel();
		match.EmptyRoomTimer = null;

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