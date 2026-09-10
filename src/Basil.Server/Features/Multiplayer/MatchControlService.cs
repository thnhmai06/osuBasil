using Basil.Server.Features.Chat;
using Basil.Server.Features.Irc;
using Basil.Server.Features.Beatmaps;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Features.Bot;
using Basil.Server.Shared.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Server.Features.Multiplayer;

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
	MatchMembership matchMembership,
	MatchLifecycle matchLifecycle,
	IMatchRepository matchRepository,
	IBeatmapRepository beatmapRepository,
	ISessionRegistry<GameSession> gameRegistry,
	ISessionRegistry<IrcSession> ircRegistry,
	ILogger<MatchControlService> logger)
{
	public enum AddRefereeResult : byte
	{
		Ok,
		TargetIsBot,
		AlreadyReferee
	}

	public enum ApplySettingsResult : byte
	{
		Ok,
		BeatmapNotFound
	}

	public enum BanResult : byte
	{
		Ok,
		TargetIsReferee,
		TargetIsBot
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

	public enum RemoveRefereeResult : byte
	{
		Ok,
		NotAReferee,
		WouldLeaveEmpty,
		TargetIsCreator
	}

	public enum SetHostResult : byte
	{
		Ok,
		TargetNotInMatch
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

	public enum UnbanResult : byte
	{
		Ok,
		NotBanned
	}

	public const int MaxMatchNameLength = 50;

	/// <summary>
	///     The match's beatmap-name field (both the bancho <c>Match</c> packet and the room-created
	///     default) when no beatmap has been selected, or the previous selection was just cleared.
	/// </summary>
	/// <remarks>
	///     A blank string here used to leave the client's multiplayer room list showing an empty
	///     "Beatmap:" line, misleadingly indistinguishable from a slow-to-load real title.
	/// </remarks>
	public const string NoBeatmapSelectedName = "No beatmap selected.";

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
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	public Task SetPrivateAsync(MatchSession match, bool isPrivate, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		match.IsPrivate = isPrivate;
		logger.LogDebug("Room settings changed: MatchId={MatchId} IsPrivate={IsPrivate}", match.DbId, isPrivate);
		mutation.PublishState();
		return Task.CompletedTask;
	}

	/// <summary>Applies a new room size, clamped to the 1 through 16 range, and broadcasts the resulting state.</summary>
	/// <param name="match">The match to resize.</param>
	/// <param name="size">The desired size, clamped to the 1 through 16 range.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	public Task SetSizeAsync(MatchSession match, int size, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		size = Math.Clamp(size, 1, 16);
		ApplySize(match, size);
		logger.LogDebug("Room settings changed: MatchId={MatchId} Size={Size}", match.DbId, size);
		mutation.PublishState();
		matchLifecycle.CancelQueuedAutoStart(match);
		return Task.CompletedTask;
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

	/// <summary>Transfers hosting to another userSession and records the grant as a match event.</summary>
	/// <param name="match">The match whose host changes.</param>
	/// <param name="target">The userSession who becomes the host.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state and host.</param>
	/// <param name="cancellationToken">A token that cancels the event write.</param>
	/// <returns>
	///     <see cref="SetHostResult.Ok" /> on success, or <see cref="SetHostResult.TargetNotInMatch" /> when the
	///     target occupies no slot in this match.
	/// </returns>
	public async Task<SetHostResult> SetHostAsync(MatchSession match, GameSession target, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		if (match.GetSlot(target.Id) is null) return SetHostResult.TargetNotInMatch;

		var prevHostId = match.HostId;
		match.HostId = target.Id;
		logger.LogInformation("Host transferred: MatchId={MatchId} PrevHostId={PrevHostId} NewHostId={NewHostId}",
			match.DbId, prevHostId, target.Id);
		target.Enqueue(ServerPacketWriter.MatchTransferHost());
		mutation.PublishState();

		var prevHostName = gameRegistry.GetByUserId(prevHostId)?.Name;
		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.HostGranted,
			prevHostId, prevHostName, target.Id, target.Name,
			DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		mutation.PublishHost();
		return SetHostResult.Ok;
	}

	/// <summary>
	///     Clears the host assignment (setting the host id to <see cref="BotBootstrapService.BotId" />)
	///     and republishes the host state.
	/// </summary>
	/// <param name="match">The match whose host to clear.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state and host.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	public Task ClearHostAsync(MatchSession match, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		match.HostId = MatchSession.NoHostId;
		mutation.PublishState();
		mutation.PublishHost();
		return Task.CompletedTask;
	}

	/// <summary>
	///     Sets the room name, truncating it to <see cref="MaxMatchNameLength" /> characters, broadcasts
	///     the state, and syncs the room's chat channel topic to match.
	/// </summary>
	/// <param name="match">The match to rename.</param>
	/// <param name="name">The new room name.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	public Task SetNameAsync(MatchSession match, string name, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		if (name.Length > MaxMatchNameLength) name = name[..MaxMatchNameLength];

		match.Name = name;
		matchMembership.SyncChannelTopic(match);
		mutation.PublishState();
		return Task.CompletedTask;
	}

	/// <summary>Sets the room password and broadcasts the resulting state.</summary>
	/// <remarks>An empty string clears the password, matching <c>!mp password</c> with no argument.</remarks>
	/// <param name="match">The match to update.</param>
	/// <param name="password">The new password, or an empty string to clear it.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	public Task SetPasswordAsync(MatchSession match, string password, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		match.Password = password;
		mutation.PublishState();
		return Task.CompletedTask;
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
	/// <param name="mutation">The open mutation scope that publishes the resulting referee list.</param>
	/// <param name="cancellationToken">A token that cancels the event write.</param>
	/// <returns>
	///     <see cref="AddRefereeResult.Ok" /> on success, <see cref="AddRefereeResult.TargetIsBot" />,
	///     or <see cref="AddRefereeResult.AlreadyReferee" /> when the target already holds referee
	///     status (a no-op: no event is recorded and nothing republishes).
	/// </returns>
	public async Task<AddRefereeResult> AddRefereeAsync(int? actorId, string? actorName, MatchSession match,
		UserSession target, MatchMutationScope mutation, CancellationToken cancellationToken = default)
	{
		if (target.IsBot) return AddRefereeResult.TargetIsBot;
		if (match.IsReferee(target.Id)) return AddRefereeResult.AlreadyReferee;

		match.AddReferee(target.Id);
		logger.LogInformation("Referee added: MatchId={MatchId} ActorId={ActorId} TargetId={TargetId}",
			match.DbId, actorId, target.Id);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.RefAdded,
			actorId, actorName, target.Id, target.Name,
			DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		mutation.PublishRefs();
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
	/// <param name="mutation">The open mutation scope that publishes the resulting referee list.</param>
	/// <param name="cancellationToken">A token that cancels the event writes.</param>
	/// <returns>
	///     <see cref="SetRefereesResult.Ok" /> on success, <see cref="SetRefereesResult.WouldLeaveEmpty" />
	///     when <paramref name="targets" /> is empty, or <see cref="SetRefereesResult.WouldRemoveCreator" />
	///     when it omits the match's current creator-referee.
	/// </returns>
	public async Task<SetRefereesResult> SetRefereesAsync(
		MatchSession match,
		IReadOnlyCollection<UserSession> targets,
		MatchMutationScope mutation,
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

		mutation.PublishRefs();
		return SetRefereesResult.Ok;
	}

	/// <summary>Revokes referee status from a single userSession and records the removal as a match event.</summary>
	/// <remarks>
	///     A guard blocks removing the last referee, so a match can never reach zero referees through
	///     this path. The match's creator (<see cref="MatchSession.IsCreator" />) can never be removed
	///     either — they hold referee-equivalent authority for the room's lifetime regardless. A target
	///     who is removed while not currently seated as a player is also removed from the match's chat
	///     channel, since losing referee status also loses their only standing to be there (see
	///     <see cref="ChannelMembershipService.Join" />'s match-room gate).
	/// </remarks>
	/// <param name="actorId">The acting userSession's id, or <see langword="null" /> for a system or HTTP action.</param>
	/// <param name="actorName">The acting userSession's name, or <see langword="null" /> when unknown.</param>
	/// <param name="match">The match to update.</param>
	/// <param name="target">The referee to remove.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting referee list.</param>
	/// <param name="cancellationToken">A token that cancels the event write.</param>
	/// <returns>
	///     <see cref="RemoveRefereeResult.Ok" /> on success,
	///     <see cref="RemoveRefereeResult.NotAReferee" /> when the target holds no referee status,
	///     <see cref="RemoveRefereeResult.TargetIsCreator" /> when the target created the match, or
	///     <see cref="RemoveRefereeResult.WouldLeaveEmpty" /> when removing them would leave the room
	///     without any referees.
	/// </returns>
	public async Task<RemoveRefereeResult> RemoveOneRefereeAsync(int? actorId, string? actorName, MatchSession match,
		UserSession target, MatchMutationScope mutation, CancellationToken cancellationToken = default)
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

		mutation.PublishRefs();
		return RemoveRefereeResult.Ok;
	}

	/// <summary>
	///     Removes every live session a userSession has in a match's chat channel when they are not
	///     currently seated as a player there.
	/// </summary>
	/// <remarks>
	///     Called after a referee removal: losing referee status also loses a non-seated participant's
	///     only standing to be in the room's channel (see
	///     <see cref="ChannelMembershipService.Join" />'s match-room gate), so they
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
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	public Task SetTeamTypeWinConditionAndSizeAsync(MatchSession match, MatchTeamType teamType,
		MatchWinCondition? winCondition, int? size, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		ApplyTeamType(match, teamType);
		if (winCondition is { } wc) match.WinCondition = wc;
		if (size is { } s) ApplySize(match, s);
		logger.LogDebug(
			"Room settings changed: MatchId={MatchId} TeamType={TeamType} WinCondition={WinCondition} Size={Size}",
			match.DbId, teamType, winCondition, size);

		mutation.PublishState();
		matchLifecycle.CancelQueuedAutoStart(match);
		return Task.CompletedTask;
	}

	/// <summary>Assigns a beatmap to the match, unreadies all players, and broadcasts the resulting state.</summary>
	/// <remarks>Returns the resolved beatmap alongside the result, so callers do not need a second lookup.</remarks>
	/// <param name="match">The match to update.</param>
	/// <param name="beatmapId">The id of the beatmap to assign.</param>
	/// <param name="playmode">
	///     An optional converted game mode to play the beatmap as, instead of its own native mode.
	///     Only takes effect when the beatmap's own mode is <see cref="GameMode.Standard" /> (the only
	///     mode osu! can convert into every other ruleset); it is silently ignored for a beatmap
	///     that's already mode-specific, which then always plays as its own native mode.
	/// </param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">A token that cancels the beatmap lookup.</param>
	/// <returns>
	///     <see cref="SetMapResult.Ok" /> with the resolved beatmap, or
	///     <see cref="SetMapResult.BeatmapNotFound" /> with <see langword="null" />.
	/// </returns>
	public async Task<(SetMapResult Result, Beatmap? Beatmap)> SetMapAsync(MatchSession match, int beatmapId,
		MatchMutationScope mutation, GameMode? playmode = null, CancellationToken cancellationToken = default)
	{
		var beatmap = await beatmapRepository.FetchOneAsync(beatmapId, cancellationToken: cancellationToken);
		if (beatmap is null) return (SetMapResult.BeatmapNotFound, null);

		match.UnreadyPlayers();
		match.MapId = beatmap.Id;
		match.MapMd5 = beatmap.Md5;
		match.MapName = beatmap.FullName;
		match.Mode = playmode is not null && beatmap.Difficulty.Mode == GameMode.Standard
			? playmode.Value
			: beatmap.Difficulty.Mode;
		logger.LogDebug("Room settings changed: MatchId={MatchId} MapId={MapId}", match.DbId, beatmap.Id);
		mutation.PublishState();
		matchLifecycle.CancelQueuedAutoStart(match);
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
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	public Task SetModsAsync(MatchSession match, Mods mods, bool enableFreemod, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		if (enableFreemod && !match.Freemods) EnableFreemods(match);
		else DisableFreemods(match);
		match.Mods = mods.FilterInvalidCombos(match.Mode);

		logger.LogDebug("Room settings changed: MatchId={MatchId} Mods={Mods} Freemod={Freemod}",
			match.DbId, mods, match.Freemods);
		mutation.PublishState();
		return Task.CompletedTask;
	}

	/// <summary>
	///     Sets the match's beatmap from an optional id, leaving the current selection untouched when
	///     none is given.
	/// </summary>
	/// <remarks>
	///     A <see langword="null" /> or non-positive <paramref name="mapId" /> means "no beatmap
	///     chosen" -- ids in this schema auto-increment from 1, so 0 can never be a real beatmap, and a
	///     caller still sending the legacy <c>-1</c> sentinel is treated the same way. All three are
	///     skipped entirely rather than attempted as a lookup, which would otherwise fail with a
	///     confusing "beatmap not found" result for a caller correctly signaling "no map".
	/// </remarks>
	/// <param name="match">The match to update.</param>
	/// <param name="mapId">The beatmap id to assign, or <see langword="null" />/non-positive to leave the selection unchanged.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">A token that cancels the beatmap lookup.</param>
	/// <returns><see cref="SetMapResult.Ok" /> when skipped or applied, or <see cref="SetMapResult.BeatmapNotFound" />.</returns>
	public async Task<SetMapResult> SetMapIfProvidedAsync(MatchSession match, int? mapId, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		if (mapId is null or <= 0) return SetMapResult.Ok;

		var (result, _) = await SetMapAsync(match, mapId.Value, mutation, cancellationToken: cancellationToken);
		return result;
	}

	/// <summary>Applies match mods, treating <paramref name="freemod" /> as overriding <paramref name="mods" />.</summary>
	/// <param name="match">The match to update.</param>
	/// <param name="mods">The mods to apply when not enabling freemod.</param>
	/// <param name="freemod"><see langword="true" /> to switch the room into freemod mode instead of applying <paramref name="mods" />.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	public Task ApplyModsAsync(MatchSession match, Mods mods, bool freemod, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		return freemod
			? SetModsAsync(match, Mods.NoMod, true, mutation, cancellationToken)
			: SetModsAsync(match, mods, false, mutation, cancellationToken);
	}

	/// <summary>
	///     Applies each provided field to the match's room settings, leaving every omitted field
	///     unchanged.
	/// </summary>
	/// <param name="match">The match to update.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting state.</param>
	/// <param name="cancellationToken">A token that cancels the beatmap lookup, when <paramref name="mapId" /> is given.</param>
	/// <returns><see cref="ApplySettingsResult.Ok" />, or <see cref="ApplySettingsResult.BeatmapNotFound" /> when <paramref name="mapId" /> doesn't resolve.</returns>
	public async Task<ApplySettingsResult> ApplyPartialSettingsAsync(MatchSession match, string? name,
		string? password, bool? isPrivate, bool? isLocked, int? size, int? mapId, Mods? mods, bool? freemod,
		MatchTeamType? teamType, MatchWinCondition? winCondition, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		if (name is not null) await SetNameAsync(match, name, mutation, cancellationToken);
		if (password is not null) await SetPasswordAsync(match, password, mutation, cancellationToken);
		if (isPrivate is not null) await SetPrivateAsync(match, isPrivate.Value, mutation, cancellationToken);
		if (isLocked is not null) SetLocked(match, isLocked.Value);
		if (size is not null) await SetSizeAsync(match, size.Value, mutation, cancellationToken);

		if (mapId is not null)
		{
			var (result, _) = await SetMapAsync(match, mapId.Value, mutation, cancellationToken: cancellationToken);
			if (result == SetMapResult.BeatmapNotFound) return ApplySettingsResult.BeatmapNotFound;
		}

		if (freemod == true)
			await SetModsAsync(match, Mods.NoMod, true, mutation, cancellationToken);
		else if (mods is not null)
			await SetModsAsync(match, mods.Value, false, mutation, cancellationToken);

		if (teamType is not null || winCondition is not null)
			await SetTeamTypeWinConditionAndSizeAsync(match, teamType ?? match.TeamType, winCondition, null, mutation,
				cancellationToken);

		return ApplySettingsResult.Ok;
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
	///     present, online, or ever have joined. <see cref="MatchMembership.JoinAsync" />'s
	///     existing ban gate blocks any later join attempt (game or otherwise) by this UserId, so
	///     banning an IRC-only or fully offline participant still blocks a future real-client login.
	///     A referee and BasilBot can never be banned.
	/// </remarks>
	/// <param name="actorId">The acting userSession's id, or <see langword="null" /> for a system or HTTP action.</param>
	/// <param name="actorName">The acting userSession's name, or <see langword="null" /> when unknown.</param>
	/// <param name="match">The match to update.</param>
	/// <param name="targetUserId">The id of the userSession to ban.</param>
	/// <param name="targetName">The name of the userSession to ban, recorded on the match event.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting banlist.</param>
	/// <param name="cancellationToken">A token that cancels the leave and event write.</param>
	/// <returns>
	///     <see cref="BanResult.Ok" />, <see cref="BanResult.TargetIsReferee" />, or
	///     <see cref="BanResult.TargetIsBot" />.
	/// </returns>
	public async Task<BanResult> BanAsync(int? actorId, string? actorName, MatchSession match, int targetUserId,
		string? targetName, MatchMutationScope mutation, CancellationToken cancellationToken = default)
	{
		if (targetUserId == BotBootstrapService.BotId) return BanResult.TargetIsBot;
		if (match.IsReferee(targetUserId)) return BanResult.TargetIsReferee;

		match.AddBan(targetUserId);

		foreach (var session in OnlineSessions(targetUserId))
			if (session is GameSession { Match: not null } gameSession && gameSession.Match == match)
			{
				await matchMembership.LeaveAsync(gameSession, match, cancellationToken);
				gameSession.Enqueue(ServerPacketWriter.MatchJoinFail());
			}
			else if (session.InChannel(match.ChatChannelName))
			{
				matchMembership.LeaveMatchChat(session, match);
			}

		logger.LogInformation(
			"User kicked: MatchId={MatchId} ActorId={ActorId} TargetId={TargetId} Reason=Banned",
			match.DbId, actorId, targetUserId);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.Kicked,
			actorId, actorName, targetUserId, targetName,
			DateTimeOffset.UtcNow.UtcDateTime, "Banned"), cancellationToken);

		mutation.PublishBans();
		return BanResult.Ok;
	}

	/// <summary>Removes a userSession from the banlist and republishes the banlist.</summary>
	/// <param name="match">The match to update.</param>
	/// <param name="targetUserId">The banned userSession's id to unban.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting banlist.</param>
	/// <param name="cancellationToken">
	///     Ignored: the eventual publish is canceled by the token given to
	///     <see cref="MatchSession.BeginMutationAsync" /> when <paramref name="mutation" /> was opened.
	/// </param>
	/// <returns>
	///     <see cref="UnbanResult.Ok" /> when the userSession was unbanned, or
	///     <see cref="UnbanResult.NotBanned" /> when they were not on the banlist.
	/// </returns>
	public Task<UnbanResult> UnbanAsync(MatchSession match, int targetUserId, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		if (!match.BannedIds.Contains(targetUserId)) return Task.FromResult(UnbanResult.NotBanned);

		match.RemoveBan(targetUserId);
		logger.LogInformation("User unbanned: MatchId={MatchId} TargetId={TargetId}", match.DbId, targetUserId);
		mutation.PublishBans();
		return Task.FromResult(UnbanResult.Ok);
	}

	/// <summary>Replaces the full banlist, kicking any newly banned players who are currently seated.</summary>
	/// <remarks>
	///     This is the PUT variant. There is no empty guard: banning down to zero players is fine.
	/// </remarks>
	/// <param name="match">The match whose banlist to replace.</param>
	/// <param name="userIds">The complete set of banned userSession ids.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting banlist.</param>
	/// <param name="cancellationToken">A token that cancels the kicks.</param>
	public async Task SetBansAsync(MatchSession match, IReadOnlyCollection<int> userIds, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		var newIds = userIds.ToHashSet();
		var toRemove = match.BannedIds.Where(id => !newIds.Contains(id)).ToList();
		var toAdd = newIds.Where(id => !match.BannedIds.Contains(id)).ToList();

		foreach (var id in toRemove) match.RemoveBan(id);
		foreach (var id in toAdd) await AddBanAndKickIfSeated(match, id, cancellationToken);

		mutation.PublishBans();
	}

	/// <summary>Adds a batch of bans, kicking any newly banned players who are currently seated.</summary>
	/// <remarks>This is the PATCH variant; it only ever adds bans.</remarks>
	/// <param name="match">The match whose banlist to extend.</param>
	/// <param name="userIds">The userSession ids to ban.</param>
	/// <param name="mutation">The open mutation scope that publishes the resulting banlist.</param>
	/// <param name="cancellationToken">A token that cancels the kicks.</param>
	public async Task AddBansAsync(MatchSession match, IReadOnlyCollection<int> userIds, MatchMutationScope mutation,
		CancellationToken cancellationToken = default)
	{
		foreach (var id in userIds)
		{
			if (match.BannedIds.Contains(id)) continue;
			await AddBanAndKickIfSeated(match, id, cancellationToken);
		}

		mutation.PublishBans();
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
			MatchMembership.JoinResult.Ok => ForceInviteResult.Ok,
			MatchMembership.JoinResult.BotCannotSeat => ForceInviteResult.TargetIsBot,
			_ => ForceInviteResult.NoFreeSlot
		};
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
}