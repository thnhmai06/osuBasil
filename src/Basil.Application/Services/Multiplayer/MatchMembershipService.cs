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
///     <see cref="MatchSession.Lock" /> already held by the caller. Packet handlers own the lock's
///     lifetime, since the lock also has to span the eventual state broadcast; see
///     <see cref="MatchSession" />'s doc comment.
/// </remarks>
public sealed class MatchMembershipService(
	IMatchRegistry matchRegistry,
	IChannelRegistry channelRegistry,
	IUserSessionRegistry sessionRegistry,
	ChannelMembershipService channelMembership,
	IMatchRepository matchRepository,
	IMatchLiveEvents eventBus,
	IBeatmapRepository beatmapRepo,
	IUserRepository userRepo,
	ILogger<MatchMembershipService> logger)
{
	private const int MaxMatchNameLength = 50;

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

	/// <summary>Creates a match, persists its row, records the creation event, and seats the host in slot 0.</summary>
	/// <param name="host">The userSession creating the room.</param>
	/// <param name="data">The parsed match-create data.</param>
	/// <param name="createdViaMakeCommand">
	///     <see langword="true" /> when created via <c>!mp make</c>; otherwise,
	///     <see langword="false" />.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the persistence and join operations.</param>
	/// <returns>The new <see cref="MatchSession" />.</returns>
	public async Task<MatchSession> CreateAsync(UserSession host, MatchState data,
		bool createdViaMakeCommand = false, CancellationToken cancellationToken = default)
	{
		var match = await matchRegistry.CreateAsync(data, host.Id, createdViaMakeCommand, cancellationToken);
		logger.LogInformation(
			"+ Match created: MatchId={MatchId} HostId={HostId} Name={Name}", match.DbId, host.Id, match.Name);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.Created,
			host.Id, host.Name, null, null,
			DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		await match.Lock.WaitAsync(cancellationToken);
		try
		{
			await JoinAsync(host, match, data.Password, cancellationToken);
		}
		finally
		{
			match.Lock.Release();
		}

		return match;
	}

	/// <summary>Creates a match with nobody in it, persisting its row and recording the creation event.</summary>
	/// <remarks>
	///     Backs the <c>api.</c> host's <c>POST /match</c>. No chat "sender" exists over HTTP, so there
	///     is no <see cref="UserSession" /> to auto-join into slot 0 the way <see cref="CreateAsync" /> does for
	///     <c>!mp make</c>. <see cref="MatchSession.HostId" /> stays 0 and the referee list stays empty
	///     until a caller assigns them via <c>PATCH /match/{id}/settings</c>, the <c>host</c> action,
	///     or the <c>addref</c> action. It is marked <see cref="MatchSession.CreatedViaMakeCommand" />
	///     for the same reason <c>!mp make</c> rooms are: it should persist until explicitly closed,
	///     not auto-teardown the moment a client briefly joins and leaves.
	/// </remarks>
	/// <param name="data">The parsed match-create data.</param>
	/// <param name="cancellationToken">A token that cancels the persistence operations.</param>
	/// <returns>The new <see cref="MatchSession" />.</returns>
	public async Task<MatchSession> CreateEmptyAsync(MatchState data,
		CancellationToken cancellationToken = default)
	{
		var match = await matchRegistry.CreateAsync(data, BotBootstrapService.BotId, true, cancellationToken);

		logger.LogInformation("+ Match created: MatchId={MatchId} HostId=0 Name={Name} (via HTTP)",
			match.DbId, match.Name);

		await matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.Created,
			null, null, null, null, DateTimeOffset.UtcNow.UtcDateTime, "Created via HTTP API"), cancellationToken);

		return match;
	}

	/// <summary>Seats a <see cref="UserSession" /> in a match after applying every join gate.</summary>
	/// <remarks>
	///     Rejects the join, sending a <c>MatchJoinFail</c> packet, when the userSession is already in a
	///     match, is a tourney client, is banned, or the room is locked; when the room is private and
	///     the userSession holds no staff privileges or invite; when the password is wrong; or when no free
	///     slot exists. The host bypasses the free-slot check and always takes slot 0.
	/// </remarks>
	/// <param name="userSession">The userSession joining.</param>
	/// <param name="match">The match to join.</param>
	/// <param name="password">The password supplied by the userSession.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	/// <returns><see langword="true" /> when the userSession was seated; otherwise, <see langword="false" />.</returns>
	public async Task<bool> JoinAsync(
		UserSession userSession, MatchSession match, string password,
		CancellationToken cancellationToken = default)
	{
		if (userSession.Match is not null || match.TourneyClients.Contains(userSession.Id) ||
		    match.BannedIds.Contains(userSession.Id) ||
		    match.IsLocked)
		{
			var reason = userSession.Match is not null ? "AlreadyInMatch"
				: match.TourneyClients.Contains(userSession.Id) ? "TourneyClient"
				: match.BannedIds.Contains(userSession.Id) ? "Banned"
				: "Locked";
			logger.LogDebug("Join rejected: MatchId={MatchId} UserId={UserId} Reason={Reason}",
				match.DbId, userSession.Id, reason);
			userSession.Enqueue(ServerPacketWriter.MatchJoinFail());
			return false;
		}

		if (match.IsPrivate && (userSession.Privilege & UserPrivileges.Staff) == 0 &&
		    !match.InvitedIds.Contains(userSession.Id))
		{
			logger.LogDebug("Join rejected: MatchId={MatchId} UserId={UserId} Reason=Private", match.DbId,
				userSession.Id);
			userSession.Enqueue(ServerPacketWriter.MatchJoinFail());
			return false;
		}

		int slotId;
		if (userSession.Id != match.HostId)
		{
			if (password != match.Password && (userSession.Privilege & UserPrivileges.Staff) == 0)
			{
				logger.LogDebug("Join rejected: MatchId={MatchId} UserId={UserId} Reason=WrongPassword",
					match.DbId, userSession.Id);
				userSession.Enqueue(ServerPacketWriter.MatchJoinFail());
				return false;
			}

			var free = match.GetFreeSlotId();
			if (free is null)
			{
				logger.LogDebug("Join rejected: MatchId={MatchId} UserId={UserId} Reason=Full", match.DbId,
					userSession.Id);
				userSession.Enqueue(ServerPacketWriter.MatchJoinFail());
				return false;
			}

			slotId = free.Value;
		}
		else
		{
			slotId = 0;
		}

		return await OccupySlot(userSession, match, slotId, cancellationToken);
	}

	/// <summary>Seats a userSession directly, bypassing every join gate.</summary>
	/// <remarks>
	///     Server-initiated seating for a force-invite (<see cref="MatchControlService.ForceInviteAsync" />).
	///     Password, private, locked, and ban gates are not re-checked here; the caller already did.
	///     Fails only when the userSession is already in a match or the room is full.
	/// </remarks>
	/// <param name="userSession">The userSession to seat.</param>
	/// <param name="match">The match to seat into.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	/// <returns><see langword="true" /> when the userSession was seated; otherwise, <see langword="false" />.</returns>
	public async Task<bool> ForceJoinAsync(UserSession userSession, MatchSession match,
		CancellationToken cancellationToken = default)
	{
		if (userSession.Match is not null) return false;

		var free = match.GetFreeSlotId();
		return free is not null && await OccupySlot(userSession, match, free.Value, cancellationToken);
	}

	/// <summary>Occupies a specific slot, runs the shared join tail, and broadcasts the new state.</summary>
	/// <remarks>
	///     Shared by <see cref="JoinAsync" /> and <see cref="ForceJoinAsync" />. The tail covers the
	///     channel join, the team default, the slot fields, the <c>MatchJoinSuccess</c> packet, the
	///     state broadcast, and the <c>PlayerJoined</c> event.
	/// </remarks>
	/// <param name="userSession">The userSession being seated.</param>
	/// <param name="match">The match being joined.</param>
	/// <param name="slotId">The 0-based slot index to occupy.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	/// <returns>
	///     <see langword="true" /> when the userSession was seated; otherwise, <see langword="false" /> when the channel is
	///     missing or the channel join failed.
	/// </returns>
	private async Task<bool> OccupySlot(UserSession userSession, MatchSession match, int slotId,
		CancellationToken cancellationToken = default)
	{
		var channel = channelRegistry.GetByName(match.ChatChannelName);
		if (channel is null || !channelMembership.Join(userSession, channel)) return false;

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

		userSession.Enqueue(ServerPacketWriter.MatchJoinSuccess(match.ToPacket()));
		await EnqueueStateAsync(match, cancellationToken: cancellationToken);

		logger.LogInformation("+ User joined match: MatchId={MatchId} UserId={UserId} SlotId={SlotId}",
			match.DbId, userSession.Id, slotId);

		_ = matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.PlayerJoined,
			userSession.Id, userSession.Name, null,
			null, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		return true;
	}

	/// <summary>Parts a userSession from a match, transferring the host when needed and tearing the room down when it empties.</summary>
	/// <param name="userSession">The userSession leaving.</param>
	/// <param name="match">The match being left.</param>
	/// <param name="cancellationToken">A token that cancels the state broadcast.</param>
	public async Task LeaveAsync(UserSession userSession, MatchSession match,
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

		if (match.Slots.All(s => s.Empty) && !match.CreatedViaMakeCommand)
		{
			TeardownMatch(match);
			logger.LogInformation("- Match auto-torn-down (empty): MatchId={MatchId}", match.DbId);
		}
		else
		{
			if (userSession.Id == match.HostId)
			{
				prevHostId = match.HostId;
				var newHostSlot = match.Slots.FirstOrDefault(s => !s.Empty);
				if (newHostSlot is not null)
				{
					newHostId = newHostSlot.PlayerId!.Value;
					match.HostId = newHostId.Value;
					hostTransfer = true;
					sessionRegistry.GetById(match.HostId)?.Enqueue(ServerPacketWriter.MatchTransferHost());
				}
			}

			await EnqueueStateAsync(match, cancellationToken: cancellationToken);
		}

		userSession.Match = null;

		logger.LogInformation("- User left match: MatchId={MatchId} UserId={UserId}", match.DbId, userSession.Id);

		_ = matchRepository.CreateEventAsync(new MatchEvent(
			match.DbId, (int)MatchEventType.PlayerLeft,
			userSession.Id, userSession.Name, null,
			null, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

		if (hostTransfer)
		{
			logger.LogInformation(
				"Host transferred on leave: MatchId={MatchId} PrevHostId={PrevHostId} NewHostId={NewHostId}",
				match.DbId, prevHostId, newHostId);

			var prevHostName = prevHostId is not null
				? sessionRegistry.GetById(prevHostId.Value)?.Name
				: null;
			var newHostName = newHostId is not null
				? sessionRegistry.GetById(newHostId.Value)?.Name
				: null;
			_ = matchRepository.CreateEventAsync(new MatchEvent(
				match.DbId, (int)MatchEventType.HostGranted,
				prevHostId, prevHostName, newHostId, newHostName,
				DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);
		}
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

			var player = sessionRegistry.GetById(playerId);
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

		var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
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
			var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
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
			match, sessionRegistry, userRepo, beatmapRepo, cancellationToken);
		eventBus.PublishMain(match.DbId, match.MainSnapshot.Publish(mainSnapshot));

		var settings = await MatchLiveSnapshotBuilder.BuildSettings(
			match, sessionRegistry, userRepo, beatmapRepo, cancellationToken);
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
		var host = await MatchLiveSnapshotBuilder.BuildHost(match, sessionRegistry, userRepo, cancellationToken);
		var delta = match.HostSnapshot.Publish(host);
		eventBus.PublishHost(match.DbId, delta);
	}

	/// <summary>Rebuilds and republishes the referee list snapshot channel.</summary>
	/// <param name="match">The match whose referees to publish.</param>
	/// <param name="cancellationToken">A token that cancels the referee lookups.</param>
	public async Task PublishRefsAsync(MatchSession match, CancellationToken cancellationToken = default)
	{
		var refs = await MatchLiveSnapshotBuilder.BuildRefs(
			match, sessionRegistry, userRepo, cancellationToken);
		var delta = match.RefsSnapshot.Publish(refs);
		eventBus.PublishRefs(match.DbId, delta);
	}

	/// <summary>Rebuilds and republishes the banlist snapshot channel.</summary>
	/// <param name="match">The match whose banlist to publish.</param>
	/// <param name="cancellationToken">A token that cancels the ban lookups.</param>
	public async Task PublishBansAsync(MatchSession match, CancellationToken cancellationToken = default)
	{
		var bans = await MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry, userRepo, cancellationToken);
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
		var slots = await MatchLiveSnapshotBuilder.BuildSlots(match, sessionRegistry, userRepo, cancellationToken);
		var delta = match.SlotsSnapshot.Publish(slots);
		eventBus.PublishSlots(match.DbId, delta);
	}

	private void TeardownMatch(MatchSession match)
	{
		match.PendingTimer?.Cancel();
		match.PendingTimer = null;

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