using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Services.Bot;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Provides the room-adjustment mutations shared by every <c>!mp</c> subcommand (see
///     <c>MpCommandService</c>) and the matching HTTP write routes on the <c>api.</c> host.
/// </summary>
/// <remarks>
///     Extracted so both surfaces call the identical state-mutation and broadcast code instead of
///     duplicating it. Callers own everything surface-specific: resolving a target userSession (chat
///     resolves by name via <see cref="ISessionRegistry{TSession}.GetByName" />, HTTP resolves by
///     numeric id via <see cref="ISessionRegistry{TSession}.GetByUserId" />), parsing and validating raw
///     input, and formatting a reply or response from the result. Every method here assumes the
///     caller already holds the match's <see cref="MatchSession.Lock" /> for the whole
///     read-mutate-broadcast sequence, exactly like every packet handler and <c>MpCommandService</c>'s
///     own <c>RunLockedAsync</c> wrapper already do. This class never acquires the lock itself.
/// </remarks>
public sealed class MatchControlService(
	MatchMembershipService matchMembership,
	IMatchRepository matchRepository,
	IBeatmapRepository beatmapRepository,
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	ILogger<MatchControlService> logger)
{
	public enum AddRefereeResult : byte
	{
		Ok,
		TargetIsBot
	}

	public enum AbortResult : byte
	{
		Ok,
		NotInProgress
	}

	public enum AbortTimerResult : byte
	{
		Ok,
		NoTimerRunning
	}

	public enum ForceInviteResult : byte
	{
		Ok,
		NoFreeSlot,
		TargetBanned,
		TargetInAnotherMatch,
		TargetIsBot
	}

	public enum InviteResult : byte
	{
		Ok,
		TargetAlreadyInRoom,
		TargetIsBot
	}

	public enum KickResult : byte
	{
		Ok,
		TargetNotInMatch,
		TargetIsReferee,
		TargetIsBot
	}

	public enum BanResult : byte
	{
		Ok,
		TargetIsReferee,
		TargetIsBot
	}

	public enum MoveResult : byte
	{
		Ok,
		DestinationNotOpen,
		TargetNotInMatch
	}

	public enum RemoveRefereeResult : byte
	{
		Ok,
		NotAReferee,
		WouldLeaveEmpty,
		TargetIsCreator
	}

	public enum SetMapResult : byte
	{
		Ok,
		BeatmapNotFound
	}

	public enum SetRefereesResult : byte
	{
		Ok,
		WouldLeaveEmpty,
		WouldRemoveCreator
	}

	public enum SetSlotsResult : byte
	{
		Ok,
		PlayerCountMismatch,
		UnknownUserId,
		SlotOccupiedAndLocked
	}

	public enum StartResult : byte
	{
		AlreadyInProgress,
		Started,
		CountdownQueued,
		BeatmapMissing
	}

	public enum TeamResult : byte
	{
		Ok,
		TargetNotInMatch
	}

	public enum UnbanResult : byte
	{
		Ok,
		NotBanned
	}

	public const int MaxMatchNameLength = 50;
	private const int PeriodicReminderIntervalSeconds = 60;
	private const int NearTotalIgnoreWindowSeconds = 5;

	/// <summary>
	///     Announcement seconds for a <c>!mp start</c> countdown, ticking down to 3 before going silent until
	///     "Good luck, have fun!"
	/// </summary>
	private static readonly int[] StartCheckpoints = [60, 30, 10, 5, 4, 3];

	/// <summary>Announcement seconds for a <c>!mp timer</c> countdown, which skips the fast final tick entirely.</summary>
	private static readonly int[] TimerOnlyCheckpoints = [60, 30, 10, 5];

	/// <summary>Sets whether the room accepts new players.</summary>
	/// <remarks>
	///     Unlike every other mutation in this class, no <c>EnqueueState</c> broadcast happens here,
	///     matching the pre-existing <c>!mp lock</c>/<c>!mp unlock</c> behavior.
	/// </remarks>
	/// <param name="match">The match to update.</param>
	/// <param name="locked"><see langword="true" /> to block new joins; otherwise, <see langword="false" />.</param>
	public static void SetLocked(MatchSession match, bool locked)
	{
		match.IsLocked = locked;
	}

	/// <summary>Sets whether the room is private and broadcasts the resulting state.</summary>
	/// <param name="match">The match to update.</param>
	/// <param name="isPrivate"><see langword="true" /> to make the room private; otherwise, <see langword="false" />.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	public async Task SetPrivateAsync(MatchSession match, bool isPrivate, CancellationToken cancellationToken = default)
	{
		match.IsPrivate = isPrivate;
		logger.LogDebug("Room settings changed: MatchId={MatchId} IsPrivate={IsPrivate}", match.DbId, isPrivate);
		await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
	}

	/// <summary>Applies a new room size, clamped to the 1 through 16 range, and broadcasts the resulting state.</summary>
	/// <param name="match">The match to resize.</param>
	/// <param name="size">The desired size, clamped to the 1 through 16 range.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	public async Task SetSizeAsync(MatchSession match, int size, CancellationToken cancellationToken = default)
	{
		size = Math.Clamp(size, 1, 16);
		ApplySize(match, size);
		logger.LogDebug("Room settings changed: MatchId={MatchId} Size={Size}", match.DbId, size);
		await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
		matchMembership.CancelQueuedAutoStart(match);
	}

	/// <summary>
	///     Opens or locks slots so the match exposes exactly <paramref name="size" /> open slots, preserving any
	///     occupants.
	/// </summary>
	/// <param name="match">The match whose slots to adjust.</param>
	/// <param name="size">The target number of open slots.</param>
	public static void ApplySize(MatchSession match, int size)
	{
		for (var i = 0; i < 16; i++)
		{
			var slot = match.Slots[i];
			if (!slot.Empty) continue;

			if (i >= size && slot.Status == SlotStatus.Open) slot.Status = SlotStatus.Locked;
			else if (i < size && slot.Status == SlotStatus.Locked) slot.Status = SlotStatus.Open;
		}
	}

	/// <summary>Moves a userSession into an open destination slot and vacates their previous one.</summary>
	/// <remarks><paramref name="destSlotIndex" /> is 0-based; callers convert from their own 1-based input.</remarks>
	/// <param name="match">The match whose slots to rearrange.</param>
	/// <param name="target">The userSession to move.</param>
	/// <param name="destSlotIndex">The 0-based index of the destination slot.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	/// <returns>
	///     <see cref="MoveResult.Ok" /> on success, <see cref="MoveResult.DestinationNotOpen" /> when the
	///     destination slot is not open, or <see cref="MoveResult.TargetNotInMatch" /> when the target
	///     occupies no slot in this match.
	/// </returns>
	public async Task<MoveResult> MoveSlotAsync(MatchSession match, UserSession target, int destSlotIndex,
		CancellationToken cancellationToken = default)
	{
		var destSlot = match.Slots[destSlotIndex];
		if (destSlot.Status != SlotStatus.Open) return MoveResult.DestinationNotOpen;

		var sourceSlot = match.GetSlot(target.Id);
		if (sourceSlot is null) return MoveResult.TargetNotInMatch;

		destSlot.CopyFrom(sourceSlot);
		sourceSlot.Reset();
		await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
		return MoveResult.Ok;
	}

	/// <summary>Transfers hosting to another userSession and records the grant as a match event.</summary>
	/// <param name="match">The match whose host changes.</param>
	/// <param name="target">The userSession who becomes the host.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast and host publish.</param>
	public async Task SetHostAsync(MatchSession match, GameSession target,
		CancellationToken cancellationToken = default)
	{
		var prevHostId = match.HostId;
		match.HostId = target.Id;
		logger.LogInformation("Host transferred: MatchId={MatchId} PrevHostId={PrevHostId} NewHostId={NewHostId}",
			match.DbId, prevHostId, target.Id);
		target.Enqueue(ServerPacketWriter.MatchTransferHost());
		await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);

		var prevHostName = gameRegistry.GetByUserId(prevHostId)?.Name;
		_ = matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.HostGranted,
			prevHostId, prevHostName, target.Id, target.Name,
			DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		await matchMembership.PublishHostAsync(match, cancellationToken);
	}

	/// <summary>
	///     Clears the host assignment (setting the host id to <see cref="BotBootstrapService.BotId" />)
	///     and republishes the host state.
	/// </summary>
	/// <param name="match">The match whose host to clear.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast and host publish.</param>
	public async Task ClearHostAsync(MatchSession match, CancellationToken cancellationToken = default)
	{
		match.HostId = MatchSession.NoHostId;
		await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
		await matchMembership.PublishHostAsync(match, cancellationToken);
	}

	/// <summary>
	///     Sets the room name, truncating it to <see cref="MaxMatchNameLength" /> characters, broadcasts
	///     the state, and syncs the room's chat channel topic to match.
	/// </summary>
	/// <param name="match">The match to rename.</param>
	/// <param name="name">The new room name.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	public async Task SetNameAsync(MatchSession match, string name, CancellationToken cancellationToken = default)
	{
		if (name.Length > MaxMatchNameLength) name = name[..MaxMatchNameLength];

		match.Name = name;
		matchMembership.SyncChannelTopic(match);
		await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
	}

	/// <summary>Sets the room password and broadcasts the resulting state.</summary>
	/// <remarks>An empty string clears the password, matching <c>!mp password</c> with no argument.</remarks>
	/// <param name="match">The match to update.</param>
	/// <param name="password">The new password, or an empty string to clear it.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	public async Task SetPasswordAsync(MatchSession match, string password,
		CancellationToken cancellationToken = default)
	{
		match.Password = password;
		await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
	}

	/// <summary>Sends a match invite to another userSession and records them as invited.</summary>
	/// <param name="sender">The userSession sending the invite.</param>
	/// <param name="match">The match being invited to.</param>
	/// <param name="target">The userSession being invited.</param>
	/// <returns>
	///     <see cref="InviteResult.Ok" /> when the invite was sent, or
	///     <see cref="InviteResult.TargetAlreadyInRoom" /> when the target is already in the match.
	/// </returns>
	public static InviteResult Invite(UserSession sender, MatchSession match, GameSession target)
	{
		if (target.IsBot) return InviteResult.TargetIsBot;
		if (target.Match == match) return InviteResult.TargetAlreadyInRoom;

		match.AddInvite(target.Id);
		target.Enqueue(ServerPacketWriter.MatchInvite(sender.Id, sender.Name, match.Embed, target.Name));
		return InviteResult.Ok;
	}

	/// <summary>Grants referee status to a single userSession and records the grant as a match event.</summary>
	/// <param name="actorId">The acting userSession's id, or <see langword="null" /> for a system or HTTP action.</param>
	/// <param name="actorName">The acting userSession's name, or <see langword="null" /> when unknown.</param>
	/// <param name="match">The match to update.</param>
	/// <param name="target">The userSession to grant referee status.</param>
	/// <param name="cancellationToken">A token that cancels the event writes and referee publication.</param>
	public async Task<AddRefereeResult> AddRefereeAsync(int? actorId, string? actorName, MatchSession match,
		UserSession target, CancellationToken cancellationToken = default)
	{
		if (target.IsBot) return AddRefereeResult.TargetIsBot;

		match.AddReferee(target.Id);
		logger.LogInformation("Referee added: MatchId={MatchId} ActorId={ActorId} TargetId={TargetId}",
			match.DbId, actorId, target.Id);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.RefAdded,
			actorId, actorName, target.Id, target.Name,
			DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		await matchMembership.PublishRefsAsync(match, cancellationToken);
		return AddRefereeResult.Ok;
	}

	/// <summary>Replaces the full referee list and records the resulting add and remove events.</summary>
	/// <remarks>
	///     This is the PUT variant. An empty <paramref name="targets" /> collection is rejected with
	///     <see cref="SetRefereesResult.WouldLeaveEmpty" /> (HTTP 409), and a collection that omits a
	///     current referee who is the match's creator is rejected with
	///     <see cref="SetRefereesResult.WouldRemoveCreator" /> (also HTTP 409) — either way, the room
	///     never ends up without any referees. A referee dropped by this replace who is not currently
	///     seated as a player is also removed from the match's chat channel (see
	///     <see cref="RemoveOneRefereeAsync" /> for why).
	/// </remarks>
	/// <param name="match">The match whose referees to replace.</param>
	/// <param name="targets">The complete set of players to keep as referees.</param>
	/// <param name="cancellationToken">A token that cancels the event writes and the referee publishes.</param>
	/// <returns>
	///     <see cref="SetRefereesResult.Ok" /> on success, <see cref="SetRefereesResult.WouldLeaveEmpty" />
	///     when <paramref name="targets" /> is empty, or <see cref="SetRefereesResult.WouldRemoveCreator" />
	///     when it omits the match's current creator-referee.
	/// </returns>
	public async Task<SetRefereesResult> SetRefereesAsync(
		MatchSession match,
		IReadOnlyCollection<UserSession> targets,
		CancellationToken cancellationToken = default)
	{
		if (targets.Count == 0) return SetRefereesResult.WouldLeaveEmpty;

		var newIds = targets.Select(t => t.Id).ToHashSet();
		if (match.CreatorId is { } creatorId && match.Referees.Contains(creatorId) && !newIds.Contains(creatorId))
			return SetRefereesResult.WouldRemoveCreator;

		var toRemove = match.Referees.Where(id => !newIds.Contains(id)).ToList();
		var toAdd = targets.Where(t => !match.Referees.Contains(t.Id)).ToList();

		foreach (var id in toRemove)
		{
			var removedName = ((UserSession?)gameRegistry.GetByUserId(id) ?? ircRegistry.GetByUserId(id))?.Name;
			match.RemoveReferee(id);
			KickFromChatIfUnseated(match, id);
			await matchRepository.CreateEventAsync(new MatchEvent(
					match.DbId, (int)MatchEventType.RefRemoved,
					null, null, id, removedName, DateTimeOffset.UtcNow.UtcDateTime, null),
				cancellationToken);
		}

		foreach (var target in toAdd)
		{
			match.AddReferee(target.Id);
			await matchRepository.CreateEventAsync(new MatchEvent(
				match.DbId, (int)MatchEventType.RefAdded,
				null, null, target.Id, target.Name, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);
		}

		await matchMembership.PublishRefsAsync(match, cancellationToken);
		return SetRefereesResult.Ok;
	}

	/// <summary>Adds a batch of referees without removing any existing ones.</summary>
	/// <remarks>
	///     This is the PATCH variant. Since it only ever adds referees, it can never trigger the
	///     empty guard.
	/// </remarks>
	/// <param name="match">The match whose referees to extend.</param>
	/// <param name="targets">The players to grant referee status.</param>
	/// <param name="cancellationToken">A token that cancels the event writes and the referee publishes.</param>
	public async Task AddRefereesAsync(MatchSession match, IReadOnlyCollection<UserSession> targets,
		CancellationToken cancellationToken = default)
	{
		foreach (var target in targets)
		{
			if (match.Referees.Contains(target.Id)) continue;

			match.AddReferee(target.Id);
			await matchRepository.CreateEventAsync(new MatchEvent(
				match.DbId, (int)MatchEventType.RefAdded,
				null, null, target.Id, target.Name, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);
		}

		await matchMembership.PublishRefsAsync(match, cancellationToken);
	}

	/// <summary>Revokes referee status from a single userSession and records the removal as a match event.</summary>
	/// <remarks>
	///     A guard blocks removing the last referee, so a match can never reach zero referees through
	///     this path. The match's creator (<see cref="MatchSession.IsCreator" />) can never be removed
	///     either — they hold referee-equivalent authority for the room's lifetime regardless. A target
	///     who is removed while not currently seated as a player is also removed from the match's chat
	///     channel, since losing referee status also loses their only standing to be there (see
	///     <see cref="Sessions.Channels.ChannelMembershipService.Join" />'s match-room gate).
	/// </remarks>
	/// <param name="actorId">The acting userSession's id, or <see langword="null" /> for a system or HTTP action.</param>
	/// <param name="actorName">The acting userSession's name, or <see langword="null" /> when unknown.</param>
	/// <param name="match">The match to update.</param>
	/// <param name="target">The referee to remove.</param>
	/// <param name="cancellationToken">A token that cancels the event writes and referee publication.</param>
	/// <returns>
	///     <see cref="RemoveRefereeResult.Ok" /> on success,
	///     <see cref="RemoveRefereeResult.NotAReferee" /> when the target holds no referee status,
	///     <see cref="RemoveRefereeResult.TargetIsCreator" /> when the target created the match, or
	///     <see cref="RemoveRefereeResult.WouldLeaveEmpty" /> when removing them would leave the room
	///     without any referees.
	/// </returns>
	public async Task<RemoveRefereeResult> RemoveOneRefereeAsync(int? actorId, string? actorName, MatchSession match,
		UserSession target, CancellationToken cancellationToken = default)
	{
		if (!match.Referees.Contains(target.Id)) return RemoveRefereeResult.NotAReferee;
		if (match.IsCreator(target.Id)) return RemoveRefereeResult.TargetIsCreator;
		if (match.Referees.Count == 1) return RemoveRefereeResult.WouldLeaveEmpty;

		match.RemoveReferee(target.Id);
		KickFromChatIfUnseated(match, target.Id);
		logger.LogInformation("Referee removed: MatchId={MatchId} ActorId={ActorId} TargetId={TargetId}",
			match.DbId, actorId, target.Id);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.RefRemoved,
			actorId, actorName, target.Id, target.Name,
			DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		await matchMembership.PublishRefsAsync(match, cancellationToken);
		return RemoveRefereeResult.Ok;
	}

	/// <summary>
	///     Removes every live session a userSession has in a match's chat channel when they are not
	///     currently seated as a player there.
	/// </summary>
	/// <remarks>
	///     Called after a referee removal: losing referee status also loses a non-seated participant's
	///     only standing to be in the room's channel (see
	///     <see cref="Sessions.Channels.ChannelMembershipService.Join" />'s match-room gate), so they
	///     are parted rather than left to linger until they PART themselves. A seated player keeps
	///     reading the room's chat regardless, since being seated is its own standing.
	/// </remarks>
	/// <param name="match">The match whose chat channel to part the userSession from.</param>
	/// <param name="targetUserId">The id of the userSession who lost referee status.</param>
	private void KickFromChatIfUnseated(MatchSession match, int targetUserId)
	{
		if (match.GetSlot(targetUserId) is not null) return;

		foreach (var session in OnlineSessions(targetUserId))
			matchMembership.LeaveMatchChat(session, match);
	}

	/// <summary>Assigns a userSession's team and broadcasts the resulting state.</summary>
	/// <param name="match">The match to update.</param>
	/// <param name="target">The userSession whose team to set.</param>
	/// <param name="team">The team to assign.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	/// <returns>
	///     <see cref="TeamResult.Ok" /> on success, or <see cref="TeamResult.TargetNotInMatch" /> when
	///     the target occupies no slot in this match.
	/// </returns>
	public async Task<TeamResult> SetTeamAsync(MatchSession match, UserSession target, MatchTeam team,
		CancellationToken cancellationToken = default)
	{
		var slot = match.GetSlot(target.Id);
		if (slot is null) return TeamResult.TargetNotInMatch;

		slot.Team = team;
		logger.LogDebug("Room settings changed: MatchId={MatchId} UserId={UserId} Team={Team}",
			match.DbId, target.Id, team);
		await matchMembership.EnqueueStateAsync(match, false, cancellationToken);
		matchMembership.CancelQueuedAutoStart(match);
		return TeamResult.Ok;
	}

	/// <summary>
	///     Reassigns every occupied slot's team to fit a new <see cref="MatchTeamType" />
	///     when it differs from the current one.
	/// </summary>
	/// <param name="match">The match to update.</param>
	/// <param name="newType">The team type to apply.</param>
	public static void ApplyTeamType(MatchSession match, MatchTeamType newType)
	{
		if (match.TeamType == newType) return;

		if (newType is MatchTeamType.HeadToHead or MatchTeamType.TagCoop)
		{
			foreach (var slot in match.Slots.Where(s => s.PlayerId is not null))
				slot.Team = MatchTeam.Neutral;
		}
		else
		{
			var occupied = match.Slots
				.Where(s => s.PlayerId is not null)
				.Select((slot, index) => (slot, index));

			var split = (match.Slots.Count(s => s.PlayerId is not null) + 1) / 2;

			foreach (var (slot, index) in occupied)
				slot.Team = index < split ? MatchTeam.Red : MatchTeam.Blue;
		}

		match.TeamType = newType;
	}

	/// <summary>Applies a new team type, win condition, and size in one pass and broadcasts the resulting state.</summary>
	/// <param name="match">The match to update.</param>
	/// <param name="teamType">The team type to apply.</param>
	/// <param name="winCondition">The new win condition, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="size">The new size, or <see langword="null" /> to leave it unchanged.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	public async Task SetTeamTypeWinConditionAndSizeAsync(MatchSession match, MatchTeamType teamType,
		MatchWinCondition? winCondition, int? size, CancellationToken cancellationToken = default)
	{
		ApplyTeamType(match, teamType);
		if (winCondition is { } wc) match.WinCondition = wc;
		if (size is { } s) ApplySize(match, s);
		logger.LogDebug(
			"Room settings changed: MatchId={MatchId} TeamType={TeamType} WinCondition={WinCondition} Size={Size}",
			match.DbId, teamType, winCondition, size);

		await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
		matchMembership.CancelQueuedAutoStart(match);
	}

	/// <summary>Assigns a beatmap to the match, unreadies all players, and broadcasts the resulting state.</summary>
	/// <remarks>Returns the resolved beatmap alongside the result, so callers do not need a second lookup.</remarks>
	/// <param name="match">The match to update.</param>
	/// <param name="beatmapId">The id of the beatmap to assign.</param>
	/// <param name="cancellationToken">A token that cancels the beatmap lookup and state broadcast.</param>
	/// <returns>
	///     <see cref="SetMapResult.Ok" /> with the resolved beatmap, or
	///     <see cref="SetMapResult.BeatmapNotFound" /> with <see langword="null" />.
	/// </returns>
	public async Task<(SetMapResult Result, Beatmap? Beatmap)> SetMapAsync(MatchSession match, int beatmapId,
		CancellationToken cancellationToken = default)
	{
		var beatmap = await beatmapRepository.FetchOneAsync(beatmapId, cancellationToken: cancellationToken);
		if (beatmap is null) return (SetMapResult.BeatmapNotFound, null);

		match.UnreadyPlayers();
		match.MapId = beatmap.Id;
		match.MapMd5 = beatmap.Md5;
		match.MapName = beatmap.FullName;
		match.Mode = beatmap.Difficulty.Mode;
		logger.LogDebug("Room settings changed: MatchId={MatchId} MapId={MapId}", match.DbId, beatmap.Id);
		await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
		matchMembership.CancelQueuedAutoStart(match);
		return (SetMapResult.Ok, beatmap);
	}

	/// <summary>Applies match mods, toggling freemod mode when requested, and broadcasts the resulting state.</summary>
	/// <remarks>
	///     Mod-setting is the only place freemod toggles. It is just one of the values a caller can
	///     pass (<paramref name="enableFreemod" />), not a separate command. Passing
	///     <paramref name="enableFreemod" /> ignores <paramref name="mods" />.
	/// </remarks>
	/// <param name="match">The match to update.</param>
	/// <param name="mods">The mods to apply when not enabling freemod.</param>
	/// <param name="enableFreemod">
	///     <see langword="true" /> to switch the room into freemod mode; otherwise,
	///     <see langword="false" />.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	public async Task SetModsAsync(MatchSession match, Mods mods, bool enableFreemod,
		CancellationToken cancellationToken = default)
	{
		if (enableFreemod && !match.Freemods) EnableFreemods(match);
		else DisableFreemods(match);
		match.Mods = mods.FilterInvalidCombos(match.Mode);

		logger.LogDebug("Room settings changed: MatchId={MatchId} Mods={Mods} Freemod={Freemod}",
			match.DbId, mods, match.Freemods);
		await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
	}

	/// <summary>Switches the room into freemod mode, stripping speed-changing mods from every occupied slot.</summary>
	/// <param name="match">The match to update.</param>
	private static void EnableFreemods(MatchSession match)
	{
		if (match.Freemods) return;

		match.Freemods = true;
		foreach (var slot in match.Slots)
			if (slot.PlayerId is not null)
				slot.Mods = match.Mods & ~Mods.SpeedChangingMods;

		match.Mods &= Mods.SpeedChangingMods;
	}

	/// <summary>Switches the room out of freemod mode, folding the host slot's mods back into the room mods.</summary>
	/// <param name="match">The match to update.</param>
	private static void DisableFreemods(MatchSession match)
	{
		var hostSlot = match.GetHostSlot();
		match.Freemods = false;
		match.Mods &= Mods.SpeedChangingMods;
		if (hostSlot is not null) match.Mods |= hostSlot.Mods;

		foreach (var slot in match.Slots)
			if (slot.PlayerId is not null)
				slot.Mods = Mods.NoMod;
	}

	/// <summary>Starts the match immediately, or queues a countdown when one is requested.</summary>
	/// <remarks>
	///     A <see langword="null" /> or non-positive <paramref name="countdownSeconds" /> starts immediately instead of
	///     queuing.
	/// </remarks>
	/// <param name="match">The match to start.</param>
	/// <param name="countdownSeconds">The countdown length in seconds, or <see langword="null" /> to start immediately.</param>
	/// <param name="cancellationToken">A token that cancels the immediate start.</param>
	/// <returns>
	///     <see cref="StartResult.AlreadyInProgress" /> when the match is already running,
	///     <see cref="StartResult.CountdownQueued" /> when a countdown was queued, or
	///     <see cref="StartResult.Started" /> or <see cref="StartResult.BeatmapMissing" /> for an
	///     immediate start.
	/// </returns>
	public async Task<StartResult> StartAsync(MatchSession match, int? countdownSeconds,
		CancellationToken cancellationToken = default)
	{
		if (match.InProgress) return StartResult.AlreadyInProgress;

		if (countdownSeconds is > 0)
		{
			BeginCountdown(match, countdownSeconds.Value, true);
			return StartResult.CountdownQueued;
		}

		var started = await matchMembership.StartAsync(match, cancellationToken);
		return started ? StartResult.Started : StartResult.BeatmapMissing;
	}

	/// <summary>Starts a plain countdown that announces but never auto-starts the match when it finishes.</summary>
	/// <param name="match">The match whose timer to start.</param>
	/// <param name="seconds">The countdown length in seconds.</param>
	public void Timer(MatchSession match, int seconds)
	{
		logger.LogDebug("Timer started: MatchId={MatchId} Seconds={Seconds}", match.DbId, seconds);
		BeginCountdown(match, seconds, false);
	}

	/// <summary>Computes the descending list of seconds at which a countdown announces.</summary>
	/// <remarks>
	///     Comprises the fixed marks (which depend on <paramref name="autoStart" />) plus an extra
	///     reminder every 60 seconds for long countdowns (for example, a 5-minute timer also
	///     announces at 240, 180, and 120). A mark that is a multiple of 60, whether from the fixed
	///     list's own 60 or a periodic one, is dropped if it falls within
	///     <see cref="NearTotalIgnoreWindowSeconds" /> of <paramref name="totalSeconds" />; otherwise
	///     it would fire almost immediately after "Queued...", which is redundant. The sub-60 marks
	///     (30/10/5/4/3/2/1) are exempt from that check, since they are meant to fire close together
	///     as the final countdown ticks down.
	/// </remarks>
	/// <param name="totalSeconds">The total countdown length in seconds.</param>
	/// <param name="autoStart">
	///     <see langword="true" /> for a <c>!mp start</c> countdown; otherwise, <see langword="false" />
	///     for a plain <c>!mp timer</c>.
	/// </param>
	/// <returns>The checkpoint seconds in descending order.</returns>
	public static IReadOnlyList<int> ComputeAnnounceCheckpoints(int totalSeconds, bool autoStart = true)
	{
		var periodic = Enumerable.Range(1, int.MaxValue)
			.Select(k => k * PeriodicReminderIntervalSeconds)
			.TakeWhile(c => c < totalSeconds);
		var baseCheckpoints = autoStart ? StartCheckpoints : TimerOnlyCheckpoints;

		return
		[
			.. baseCheckpoints
				.Concat(periodic)
				.Where(c => c < totalSeconds)
				.Distinct()
				.Where(c => c % PeriodicReminderIntervalSeconds != 0 || totalSeconds - c > NearTotalIgnoreWindowSeconds)
				.OrderByDescending(c => c)
		];
	}

	/// <summary>Starts a fire-and-forget countdown, cancelling any timer already pending on the match.</summary>
	/// <remarks>
	///     The loop itself (<see cref="CountdownLoopAsync" />) only holds <see cref="MatchSession.Lock" />
	///     briefly at its final tick, never across a <c>Task.Delay</c>, matching this codebase's rule
	///     against holding the lock across an unrelated await.
	/// </remarks>
	/// <param name="match">The match whose countdown to start.</param>
	/// <param name="totalSeconds">The countdown length in seconds.</param>
	/// <param name="autoStart">
	///     <see langword="true" /> to start the match when the countdown finishes; otherwise,
	///     <see langword="false" />.
	/// </param>
	private void BeginCountdown(MatchSession match, int totalSeconds, bool autoStart)
	{
		match.PendingTimer?.Cancel();

		var cts = new CancellationTokenSource();
		match.PendingTimer = cts;
		match.PendingTimerIsAutoStart = autoStart;
		match.TimerStartedAt = DateTimeOffset.UtcNow;
		match.TimerTotalSeconds = totalSeconds;
		matchMembership.PublishTimer(match);
		logger.LogDebug("Countdown queued: MatchId={MatchId} Seconds={Seconds} AutoStart={AutoStart}",
			match.DbId, totalSeconds, autoStart);

		// Cuts the countdown loop's AsyncLocal inheritance (RequestId/etc.) from the request that
		// triggered it: the loop can run up to `totalSeconds` after that request has ended, so it
		// must not carry a now-stale RequestId. It pushes its own MatchId scope below instead.
		using (ExecutionContext.SuppressFlow())
		{
			_ = CountdownLoopAsync(match, totalSeconds, autoStart, cts);
		}
	}

	/// <summary>Runs a countdown, announcing at each checkpoint and optionally starting the match at zero.</summary>
	/// <param name="match">The match the countdown belongs to.</param>
	/// <param name="totalSeconds">The total countdown length in seconds.</param>
	/// <param name="autoStart">
	///     <see langword="true" /> to start the match when the countdown finishes; otherwise,
	///     <see langword="false" />.
	/// </param>
	/// <param name="cts">The token source that owns the countdown's cancellation token.</param>
	private async Task CountdownLoopAsync(MatchSession match, int totalSeconds, bool autoStart,
		CancellationTokenSource cts)
	{
		using var _ = logger.BeginScope(new Dictionary<string, object> { ["MatchId"] = match.DbId });

		var token = cts.Token;
		Announce(match,
			autoStart
				? $"Queued the match to start in {totalSeconds} seconds"
				: $"Started a {totalSeconds}-second countdown.");

		var remaining = totalSeconds;
		foreach (var checkpoint in ComputeAnnounceCheckpoints(totalSeconds, autoStart))
		{
			if (!await DelayAsync(remaining - checkpoint, token)) return;

			Announce(match, autoStart
				? $"Match starts in {checkpoint} seconds"
				: $"{checkpoint} seconds remaining");
			matchMembership.PublishTimer(match);
			remaining = checkpoint;
		}

		if (!await DelayAsync(remaining, token)) return;

		await match.Lock.WaitAsync(token);
		try
		{
			if (token.IsCancellationRequested) return;
			match.PendingTimer = null;
			match.PendingTimerIsAutoStart = false;
			match.TimerStartedAt = null;
			match.TimerTotalSeconds = null;

			if (autoStart)
			{
				var started = match.InProgress || await matchMembership.StartAsync(match, token);
				if (started) Announce(match, "Good luck, have fun!");
			}
			else
			{
				Announce(match, "Countdown finished");
			}

			matchMembership.PublishTimer(match);
		}
		finally
		{
			match.Lock.Release();
		}
	}

	/// <summary>Waits the given number of seconds and reports whether the wait was canceled.</summary>
	/// <param name="seconds">The delay length in seconds; zero or negative delays complete immediately.</param>
	/// <param name="token">A token that cancels the wait.</param>
	/// <returns>
	///     <see langword="true" /> when the full delay elapsed; otherwise, <see langword="false" /> when canceled,
	///     meaning the caller should stop.
	/// </returns>
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

	/// <summary>Posts a message into the match channel from the bot account when the bot is online.</summary>
	/// <param name="match">The match whose channel to announce into.</param>
	/// <param name="text">The message to post.</param>
	private void Announce(MatchSession match, string text)
	{
		var bot = gameRegistry.GetByUserId(BotBootstrapService.BotId);
		if (bot is null) return;

		matchMembership.EnqueueChat(match, bot.Name, bot.Id, text);
	}

	/// <summary>Cancels a pending countdown and republishes the timer state.</summary>
	/// <param name="match">The match whose timer to abort.</param>
	/// <returns>
	///     <see cref="AbortTimerResult.Ok" /> when a timer was running and was canceled, or
	///     <see cref="AbortTimerResult.NoTimerRunning" /> when none was.
	/// </returns>
	public AbortTimerResult AbortTimer(MatchSession match)
	{
		if (match.PendingTimer is null) return AbortTimerResult.NoTimerRunning;

		match.PendingTimer.Cancel();
		match.PendingTimer = null;
		match.PendingTimerIsAutoStart = false;
		match.TimerStartedAt = null;
		match.TimerTotalSeconds = null;
		logger.LogDebug("Timer aborted: MatchId={MatchId}", match.DbId);
		matchMembership.PublishTimer(match);
		return AbortTimerResult.Ok;
	}

	/// <summary>Stops an in-progress match, unreadying playing players and ending the current round.</summary>
	/// <param name="match">The match to abort.</param>
	/// <param name="cancellationToken">A token that cancels the round-end writer and state broadcast.</param>
	/// <returns>
	///     <see cref="AbortResult.Ok" /> when the match was aborted, or
	///     <see cref="AbortResult.NotInProgress" /> when it was not running.
	/// </returns>
	public async Task<AbortResult> AbortAsync(MatchSession match, CancellationToken cancellationToken = default)
	{
		if (!match.InProgress) return AbortResult.NotInProgress;

		match.UnreadyPlayers(SlotStatus.Playing);
		match.ResetPlayersLoadedStatus();
		match.InProgress = false;

		var roundId = match.CurrentRoundId;
		if (roundId is { } id)
		{
			await matchRepository.SetRoundEndedAsync(id, DateTimeOffset.UtcNow.UtcDateTime, true,
				cancellationToken);
			match.CurrentRoundId = null;
		}

		logger.LogInformation("Match aborted: MatchId={MatchId} RoundId={RoundId}", match.DbId, roundId);
		matchMembership.Enqueue(match, ServerPacketWriter.MatchAbort(), false);
		await matchMembership.EnqueueStateAsync(match, cancellationToken: cancellationToken);
		return AbortResult.Ok;
	}

	/// <summary>
	///     Removes every session (game and IRC alike) a userSession currently has in the match and
	///     records the kick as a match event.
	/// </summary>
	/// <remarks>
	///     A referee can never be kicked — remove referee status first. BasilBot can never be kicked.
	///     Unlike <see cref="BanAsync" />, the target must actually be present (seated, or an IRC
	///     session in the match's chat/scoped to it) — there is nothing to kick otherwise.
	/// </remarks>
	/// <param name="actorId">The acting userSession's id, or <see langword="null" /> for a system or HTTP action.</param>
	/// <param name="actorName">The acting userSession's name, or <see langword="null" /> when unknown.</param>
	/// <param name="match">The match to update.</param>
	/// <param name="targetUserId">The id of the userSession to kick.</param>
	/// <param name="targetName">The name of the userSession to kick, recorded on the match event.</param>
	/// <param name="cancellationToken">A token that cancels the leave and event writes.</param>
	/// <returns>
	///     <see cref="KickResult.Ok" /> on success, <see cref="KickResult.TargetNotInMatch" /> when the
	///     target has no session present in this match, <see cref="KickResult.TargetIsReferee" />, or
	///     <see cref="KickResult.TargetIsBot" />.
	/// </returns>
	public async Task<KickResult> KickAsync(int? actorId, string? actorName, MatchSession match, int targetUserId,
		string? targetName, CancellationToken cancellationToken = default)
	{
		if (targetUserId == BotBootstrapService.BotId) return KickResult.TargetIsBot;
		if (match.IsReferee(targetUserId)) return KickResult.TargetIsReferee;

		var removedAny = false;
		foreach (var session in OnlineSessions(targetUserId))
		{
			if (session is GameSession { Match: not null } gameSession && gameSession.Match == match)
			{
				await matchMembership.LeaveAsync(gameSession, match, cancellationToken);
				gameSession.Enqueue(ServerPacketWriter.MatchJoinFail());
				removedAny = true;
			}
			else if (session.InChannel(match.ChatChannelName))
			{
				matchMembership.LeaveMatchChat(session, match);
				removedAny = true;
			}
		}

		if (!removedAny) return KickResult.TargetNotInMatch;

		logger.LogInformation(
			"User kicked: MatchId={MatchId} ActorId={ActorId} TargetId={TargetId} Reason=Kicked",
			match.DbId, actorId, targetUserId);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.Kicked,
			actorId, actorName, targetUserId, targetName,
			DateTimeOffset.UtcNow.UtcDateTime, "Kicked"), cancellationToken);

		return KickResult.Ok;
	}

	/// <summary>
	///     Adds a userSession to the banlist and evicts every live session they currently have in the
	///     match, regardless of whether they were online or seated at all.
	/// </summary>
	/// <remarks>
	///     Unlike <see cref="KickAsync" />, this always adds the ban — the target does not need to be
	///     present, online, or ever have joined. <see cref="MatchMembershipService.JoinAsync" />'s
	///     existing ban gate blocks any later join attempt (game or otherwise) by this UserId, so
	///     banning an IRC-only or fully offline participant still blocks a future real-client login.
	///     A referee and BasilBot can never be banned.
	/// </remarks>
	/// <param name="actorId">The acting userSession's id, or <see langword="null" /> for a system or HTTP action.</param>
	/// <param name="actorName">The acting userSession's name, or <see langword="null" /> when unknown.</param>
	/// <param name="match">The match to update.</param>
	/// <param name="targetUserId">The id of the userSession to ban.</param>
	/// <param name="targetName">The name of the userSession to ban, recorded on the match event.</param>
	/// <param name="cancellationToken">A token that cancels the leave, event write, and banlist publish.</param>
	/// <returns>
	///     <see cref="BanResult.Ok" />, <see cref="BanResult.TargetIsReferee" />, or
	///     <see cref="BanResult.TargetIsBot" />.
	/// </returns>
	public async Task<BanResult> BanAsync(int? actorId, string? actorName, MatchSession match, int targetUserId,
		string? targetName, CancellationToken cancellationToken = default)
	{
		if (targetUserId == BotBootstrapService.BotId) return BanResult.TargetIsBot;
		if (match.IsReferee(targetUserId)) return BanResult.TargetIsReferee;

		match.AddBan(targetUserId);

		foreach (var session in OnlineSessions(targetUserId))
		{
			if (session is GameSession { Match: not null } gameSession && gameSession.Match == match)
			{
				await matchMembership.LeaveAsync(gameSession, match, cancellationToken);
				gameSession.Enqueue(ServerPacketWriter.MatchJoinFail());
			}
			else if (session.InChannel(match.ChatChannelName))
			{
				matchMembership.LeaveMatchChat(session, match);
			}
		}

		logger.LogInformation(
			"User kicked: MatchId={MatchId} ActorId={ActorId} TargetId={TargetId} Reason=Banned",
			match.DbId, actorId, targetUserId);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.Kicked,
			actorId, actorName, targetUserId, targetName,
			DateTimeOffset.UtcNow.UtcDateTime, "Banned"), cancellationToken);

		await matchMembership.PublishBansAsync(match, cancellationToken);
		return BanResult.Ok;
	}

	/// <summary>Removes a userSession from the banlist and republishes the banlist.</summary>
	/// <param name="match">The match to update.</param>
	/// <param name="targetUserId">The banned userSession's id to unban.</param>
	/// <param name="cancellationToken">A token that cancels the banlist publication.</param>
	/// <returns>
	///     <see cref="UnbanResult.Ok" /> when the userSession was unbanned, or
	///     <see cref="UnbanResult.NotBanned" /> when they were not on the banlist.
	/// </returns>
	public async Task<UnbanResult> UnbanAsync(MatchSession match, int targetUserId,
		CancellationToken cancellationToken = default)
	{
		if (!match.BannedIds.Contains(targetUserId)) return UnbanResult.NotBanned;

		match.RemoveBan(targetUserId);
		logger.LogInformation("User unbanned: MatchId={MatchId} TargetId={TargetId}", match.DbId, targetUserId);
		await matchMembership.PublishBansAsync(match, cancellationToken);
		return UnbanResult.Ok;
	}

	/// <summary>Replaces the full banlist, kicking any newly banned players who are currently seated.</summary>
	/// <remarks>
	///     This is the PUT variant. There is no empty guard: banning down to zero players is fine.
	/// </remarks>
	/// <param name="match">The match whose banlist to replace.</param>
	/// <param name="userIds">The complete set of banned userSession ids.</param>
	/// <param name="cancellationToken">A token that cancels the kicks and the banlist publication.</param>
	public async Task SetBansAsync(MatchSession match, IReadOnlyCollection<int> userIds,
		CancellationToken cancellationToken = default)
	{
		var newIds = userIds.ToHashSet();
		var toRemove = match.BannedIds.Where(id => !newIds.Contains(id)).ToList();
		var toAdd = newIds.Where(id => !match.BannedIds.Contains(id)).ToList();

		foreach (var id in toRemove) match.RemoveBan(id);
		foreach (var id in toAdd) await AddBanAndKickIfSeated(match, id, cancellationToken);

		await matchMembership.PublishBansAsync(match, cancellationToken);
	}

	/// <summary>Adds a batch of bans, kicking any newly banned players who are currently seated.</summary>
	/// <remarks>This is the PATCH variant; it only ever adds bans.</remarks>
	/// <param name="match">The match whose banlist to extend.</param>
	/// <param name="userIds">The userSession ids to ban.</param>
	/// <param name="cancellationToken">A token that cancels the kicks and the banlist publication.</param>
	public async Task AddBansAsync(MatchSession match, IReadOnlyCollection<int> userIds,
		CancellationToken cancellationToken = default)
	{
		foreach (var id in userIds)
		{
			if (match.BannedIds.Contains(id)) continue;
			await AddBanAndKickIfSeated(match, id, cancellationToken);
		}

		await matchMembership.PublishBansAsync(match, cancellationToken);
	}

	/// <summary>Adds a userSession to the banlist and kicks them from the match if they are currently seated.</summary>
	/// <param name="match">The match to update.</param>
	/// <param name="userId">The userSession id to ban.</param>
	/// <param name="cancellationToken">A token that cancels the leave operation.</param>
	private async Task AddBanAndKickIfSeated(MatchSession match, int userId,
		CancellationToken cancellationToken = default)
	{
		match.AddBan(userId);

		var seated = gameRegistry.GetByUserId(userId);
		if (seated is null || seated.Match != match) return;

		await matchMembership.LeaveAsync(seated, match, cancellationToken);
		seated.Enqueue(ServerPacketWriter.MatchJoinFail());
	}

	/// <summary>Seats a userSession directly, bypassing password, private, and locked gating.</summary>
	/// <remarks>
	///     Backs <c>force: true</c> on <c>POST /matches/{matchId}/invite</c>. A banned target is still
	///     rejected; that is the one gate force does not cross.
	/// </remarks>
	/// <param name="match">The match to seat into.</param>
	/// <param name="target">The userSession to seat.</param>
	/// <param name="cancellationToken">A token that cancels the join operation.</param>
	/// <returns>
	///     <see cref="ForceInviteResult.Ok" /> when seated (or already in the room),
	///     <see cref="ForceInviteResult.NoFreeSlot" /> when the room is full,
	///     <see cref="ForceInviteResult.TargetBanned" /> when the target is banned, or
	///     <see cref="ForceInviteResult.TargetInAnotherMatch" /> when the target is already elsewhere.
	/// </returns>
	public async Task<ForceInviteResult> ForceInviteAsync(MatchSession match, GameSession target,
		CancellationToken cancellationToken = default)
	{
		if (target.IsBot) return ForceInviteResult.TargetIsBot;
		if (match.BannedIds.Contains(target.Id)) return ForceInviteResult.TargetBanned;
		if (target.Match == match) return ForceInviteResult.Ok;
		if (target.Match is not null) return ForceInviteResult.TargetInAnotherMatch;

		var joined = await matchMembership.ForceJoinAsync(target, match, cancellationToken);
		return joined switch
		{
			MatchMembershipService.JoinResult.Ok => ForceInviteResult.Ok,
			MatchMembershipService.JoinResult.BotCannotSeat => ForceInviteResult.TargetIsBot,
			_ => ForceInviteResult.NoFreeSlot
		};
	}

	/// <summary>Reassigns, re-teams, and locks slots in one atomic pass, then republishes the slot views.</summary>
	/// <remarks>
	///     Every <see cref="SlotPatchEntry.UserId" /> referenced anywhere in <paramref name="entries" />
	///     must already occupy some slot in this match (<see cref="SetSlotsResult.UnknownUserId" />
	///     otherwise). This never seats a new userSession; it only rearranges existing occupants.
	///     <paramref name="isFullReplace" /> (PUT) also requires the referenced user ids to exactly
	///     match the match's current full occupant set (<see cref="SetSlotsResult.PlayerCountMismatch" />
	///     otherwise); PATCH only touches the slots actually given. A <see cref="SlotPatchEntry.Team" />
	///     value other than the literal strings <c>"Red"</c> or <c>"Blue"</c> is a no-op: the
	///     destination slot's existing team is preserved, never reset to neutral, and never inherited
	///     from the moving userSession's previous slot.
	/// </remarks>
	/// <param name="match">The match whose slots to rearrange.</param>
	/// <param name="entries">The slot patches keyed by a 0-based slot index.</param>
	/// <param name="isFullReplace">
	///     <see langword="true" /> to require the entries to cover every occupied slot; otherwise,
	///     <see langword="false" />.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the slot-view publication.</param>
	/// <returns>
	///     <see cref="SetSlotsResult.Ok" /> on success, or
	///     <see cref="SetSlotsResult.SlotOccupiedAndLocked" />, <see cref="SetSlotsResult.UnknownUserId" />,
	///     or <see cref="SetSlotsResult.PlayerCountMismatch" /> on validation failure.
	/// </returns>
	public async Task<SetSlotsResult> SetSlotsAsync(MatchSession match,
		IReadOnlyDictionary<int, SlotPatchEntry> entries,
		bool isFullReplace, CancellationToken cancellationToken = default)
	{
		foreach (var entry in entries.Values)
			if (entry.UserId is not null && entry.Locked == true)
				return SetSlotsResult.SlotOccupiedAndLocked;

		var currentOccupantIds = match.Slots
			.Where(s => s.PlayerId is not null)
			.Select(s => s.PlayerId!.Value)
			.ToHashSet();

		var referencedUserIds = entries.Values
			.Where(e => e.UserId is not null)
			.Select(e => e.UserId!.Value)
			.ToList();

		if (referencedUserIds.Any(uid => !currentOccupantIds.Contains(uid)))
			return SetSlotsResult.UnknownUserId;

		if (isFullReplace)
		{
			var referencedSet = referencedUserIds.ToHashSet();
			if (referencedSet.Count != currentOccupantIds.Count || !referencedSet.SetEquals(currentOccupantIds))
				return SetSlotsResult.PlayerCountMismatch;
		}

		// Snapshot every slot's pre-mutation state so a swap (A<->B) can look up each userSession's
		// origin slot without being affected by the other entry's own mutation.
		var original = match.Slots.Select(s => (s.PlayerId, s.Status, s.Team, s.Mods)).ToArray();
		var destinationSlots = entries.Where(kv => kv.Value.UserId is not null).Select(kv => kv.Key).ToHashSet();

		// Vacate the previous slot of every moved userSession, unless that slot is itself a destination
		// in this same payload (a direct swap doesn't need clearing; it gets overwritten below).
		foreach (var (slotIndex, entry) in entries)
		{
			if (entry.UserId is not { } uid) continue;

			var oldIndex = Array.FindIndex(original, o => o.PlayerId == uid);
			if (oldIndex >= 0 && oldIndex != slotIndex && !destinationSlots.Contains(oldIndex))
				match.Slots[oldIndex].Reset();
		}

		foreach (var (slotIndex, entry) in entries)
		{
			var slot = match.Slots[slotIndex];

			if (entry.UserId is { } uid)
			{
				var oldIndex = Array.FindIndex(original, o => o.PlayerId == uid);
				var source = original[oldIndex];
				slot.PlayerId = uid;
				slot.Status = source.Status;
				slot.Mods = source.Mods;
			}

			if (entry.Team is "Red" or "Blue")
				slot.Team = entry.Team == "Red" ? MatchTeam.Red : MatchTeam.Blue;

			if (entry.Locked is { } locked && slot.PlayerId is null)
				slot.Status = locked ? SlotStatus.Locked : SlotStatus.Open;
		}

		logger.LogDebug("Room settings changed: MatchId={MatchId} SlotsChanged={SlotsChanged}", match.DbId,
			entries.Count);
		await matchMembership.PublishSlotsAsync(match, cancellationToken);
		return SetSlotsResult.Ok;
	}

	/// <summary>Closes a match, parting every seated userSession and tearing the room down.</summary>
	/// <param name="actorId">The acting userSession's id, or <see langword="null" /> for a system action.</param>
	/// <param name="actorName">The acting userSession's name, or <see langword="null" /> when unknown.</param>
	/// <param name="match">The match to close.</param>
	/// <param name="cancellationToken">A token that cancels the close event writes.</param>
	public async Task CloseAsync(int? actorId, string? actorName, MatchSession match,
		CancellationToken cancellationToken = default)
	{
		await matchMembership.CloseAsync(match, actorId, actorName, cancellationToken);
	}

	/// <summary>
	///     Enumerates the online sessions of a user id, a <see cref="GameSession" /> first then an
	///     <see cref="IrcSession" />, so kick/ban cleanup reaches both live connections an account may hold.
	/// </summary>
	/// <param name="userId">The user id whose live sessions to enumerate.</param>
	private IEnumerable<UserSession> OnlineSessions(int userId)
	{
		if (gameRegistry.GetByUserId(userId) is { } game) yield return game;
		if (ircRegistry.GetByUserId(userId) is { } irc) yield return irc;
	}

	/// <summary>Represents one entry in a <c>PUT</c> or <c>PATCH /matches/{matchId}/slots</c> request.</summary>
	/// <remarks>Each entry is keyed by a slot index (0-based) in the request dictionary.</remarks>
	/// <param name="UserId">
	///     The id of the userSession to move into the slot, or <see langword="null" /> to leave occupancy
	///     alone.
	/// </param>
	/// <param name="Team">
	///     The literal <c>"Red"</c> or <c>"Blue"</c> team to assign, or <see langword="null" /> to keep the
	///     current team.
	/// </param>
	/// <param name="Locked">
	///     <see langword="true" /> to lock an empty slot, <see langword="false" /> to open it, or
	///     <see langword="null" /> to leave its status alone.
	/// </param>
	public sealed record SlotPatchEntry(int? UserId, string? Team, bool? Locked);
}