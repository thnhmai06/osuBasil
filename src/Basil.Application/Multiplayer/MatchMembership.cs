using Basil.Application.Channels;
using Basil.Application.Chat;
using Basil.Application.Sessions;
using Basil.Application.Users;
using Basil.Domain.Multiplayer.Records;
using Basil.Domain.Multiplayer.Runtime;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer;

/// <summary>Seats and removes players from a match's slots.</summary>
/// <remarks>
///     Every method here that reads-then-mutates a match's slots must be called with
///     <see cref="MatchSession.Lock" /> already held by the caller; packet handlers own the lock's
///     lifetime, since it must span the eventual state broadcast (see <see cref="MatchSession" />'s
///     doc comment).
/// </remarks>
public sealed class MatchMembership(
	IChannelRegistry channelRegistry,
	ISessionRegistry<GameSession> gameRegistry,
	ChannelMembershipService channelMembership,
	IMatchNotifier notifier,
	IMatchRepository matchRepository,
	MatchLifecycle lifecycle,
	IUserCache userCache,
	ILogger<MatchMembership> logger)
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
		var userSessionUser = userCache.Resolve(userSession);
		JoinResult? rejection = userSession.IsBot ? JoinResult.BotCannotSeat
			: userSession.Match is not null ? JoinResult.AlreadyInMatch
			: match.TourneyUsers.Contains(userSessionUser) ? JoinResult.TourneyClient
			: match.BannedUsers.Contains(userSessionUser) ? JoinResult.Banned
			: match.IsLocked ? JoinResult.Locked
			: null;

		if (rejection is { } reason)
		{
			logger.LogDebug("Url rejected: MatchId={MatchId} UserId={UserId} Reason={Reason}",
				match.DbId, userSession.Id, reason);
			notifier.JoinRejected(userSession);
			return reason;
		}

		if (match.IsPrivate && (userSession.Privilege & UserPrivileges.Staff) == 0 &&
		    !match.InvitedUsers.Contains(userSessionUser))
		{
			logger.LogDebug("Url rejected: MatchId={MatchId} UserId={UserId} Reason=Private", match.DbId,
				userSession.Id);
			notifier.JoinRejected(userSession);
			return JoinResult.Private;
		}

		if (password != match.Password && (userSession.Privilege & UserPrivileges.Staff) == 0)
		{
			logger.LogDebug("Url rejected: MatchId={MatchId} UserId={UserId} Reason=WrongPassword",
				match.DbId, userSession.Id);
			notifier.JoinRejected(userSession);
			return JoinResult.WrongPassword;
		}

		var free = match.GetFreeSlotId();
		if (free is null)
		{
			logger.LogDebug("Url rejected: MatchId={MatchId} UserId={UserId} Reason=Full", match.DbId,
				userSession.Id);
			notifier.JoinRejected(userSession);
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
				.Where(s => s.User is not null)
				.GroupBy(s => s.Team)
				.ToDictionary(g => g.Key, g => g.Count());
			counts.TryGetValue(MatchTeam.Red, out var redCount);
			counts.TryGetValue(MatchTeam.Blue, out var blueCount);
			slot.Team = redCount <= blueCount ? MatchTeam.Red : MatchTeam.Blue;
		}

		slot.Status = RoomSlotStatus.NotReady;
		slot.User = userCache.Resolve(userSession);
		userSession.Match = match;

		if (match.Host is null) match.Host = userCache.Resolve(userSession);

		notifier.Joined(userSession, match);
		// SyncEmptyRoomTimer still needs the caller's lock held. The state publish itself does not
		// (ADR-004 4b follow-up) -- the caller allocates a version and publishes after releasing the
		// lock instead, since a version allocated an instant later than the mutation is still correct.
		lifecycle.SyncEmptyRoomTimer(match);

		logger.LogInformation("+ User joined match: MatchId={MatchId} UserId={UserId} Id={Id}",
			match.DbId, userSession.Id, slotId);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, MatchEventType.PlayerJoined,
			userCache.Resolve(userSession), null,
			DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		return true;
	}

	/// <summary>Parts a userSession from a match, transferring the host when needed.</summary>
	/// <remarks>
	///     No longer tears the room down when every slot empties — an empty room persists under
	///     referee control (exactly like an IRC-created one) until <see cref="MatchLifecycle.SyncEmptyRoomTimer" />'s
	///     5-minute auto-close fires or a referee runs <c>!mp close</c>.
	/// </remarks>
	/// <param name="userSession">The userSession leaving.</param>
	/// <param name="match">The match being left.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	public async Task LeaveAsync(GameSession userSession, MatchSession match,
		CancellationToken cancellationToken = default)
	{
		var userSessionUser = userCache.Resolve(userSession);
		var slot = match.GetSlot(userSessionUser);
		if (slot is null)
		{
			userSession.Match = null;
			return;
		}

		slot.Reset(slot.Status == RoomSlotStatus.Locked ? RoomSlotStatus.Locked : RoomSlotStatus.Open);

		var hostTransfer = false;
		User? prevHost = null;
		User? newHost = null;

		if (userSessionUser.Equals(match.Host))
		{
			prevHost = match.Host;
			var newHostSlot = match.Slots.FirstOrDefault(s => !s.IsEmpty);
			if (newHostSlot is not null)
			{
				newHost = newHostSlot.User!;
				match.Host = newHost;
				hostTransfer = true;
				if (gameRegistry.GetByUserId(newHost.Id) is { } newHostSession)
					notifier.HostTransferred(newHostSession);
			}
			else
			{
				match.Host = null;
			}
		}

		// Parting the chat channel broadcasts to the room's other members, and a broadcast can fail:
		// an IRC member's connection may already be gone. It runs after the host reassignment above
		// so that a failure here cannot leave the match naming a host who occupies no slot.
		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is not null) channelMembership.Part(userSession, channel);

		// SyncEmptyRoomTimer still needs the caller's lock held. The state publish itself does not
		// (ADR-004 4b follow-up) -- the caller allocates a version and publishes after releasing the
		// lock instead, since a version allocated an instant later than the mutation is still correct.
		lifecycle.SyncEmptyRoomTimer(match);

		userSession.Match = null;

		logger.LogInformation("- User left match: MatchId={MatchId} UserId={UserId}", match.DbId, userSession.Id);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, MatchEventType.PlayerLeft,
			userCache.Resolve(userSession), null,
			DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		if (hostTransfer)
		{
			logger.LogInformation(
				"Host transferred on leave: MatchId={MatchId} PrevHostId={PrevHostId} NewHostId={NewHostId}",
				match.DbId, prevHost?.Id, newHost?.Id);

			await matchRepository.CreateEventAsync(new MatchEvent(
				match.DbId, MatchEventType.HostGranted,
				prevHost, newHost,
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
	///     <see cref="ChannelMembershipService.Join" /> for what this bypasses.
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
	///     <see cref="ChannelMembershipService.SyncTopic" />).
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
}