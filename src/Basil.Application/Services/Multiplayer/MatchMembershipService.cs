using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Services.Bot;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.Services.Multiplayer;

/// <summary>
///     Ported from Player.join_match/leave_match plus Match.enqueue/enqueue_state. Every method here
///     that reads-then-mutates a match's slots or settings must be called with <see cref="MatchSession.Lock" />
///     already held by the caller (packet handlers own the lock's lifetime, since the lock also has
///     to span the eventual `enqueue_state` broadcast — see MatchSession's doc comment).
/// </summary>
public sealed class MatchMembershipService(
    IMatchRegistry matchRegistry,
    IChannelRegistry channelRegistry,
    IPlayerSessionRegistry sessionRegistry,
    ChannelMembershipService channelMembership,
    IMatchPersistenceRepository matchPersistence,
    IMatchLiveEvents eventBus,
    IMapRepository mapRepo)
{
    private const int MaxMatchNameLength = 50;

    public static bool ValidateMatchData(ReadMatchResult data, int expectedHostId)
    {
        return data.HostId == expectedHostId && data.Name.Length <= MaxMatchNameLength;
    }

    public static string ChannelNameFor(int matchId)
    {
        return $"#multi_{matchId}";
    }

    public static MatchSession BuildNew(int id, ReadMatchResult data, int hostId, bool createdViaMakeCommand = false)
    {
        return new MatchSession(
            id,
            data.Name,
            data.Password,
            data.MapName,
            data.MapId,
            data.MapMd5,
            hostId,
            (GameMode)data.Mode,
            (Mods)data.Mods,
            (MatchWinCondition)data.WinCondition,
            (MatchTeamType)data.TeamType,
            data.FreeMods,
            data.Seed,
            ChannelNameFor(id),
            createdViaMakeCommand);
    }

    public void RegisterChannel(MatchSession match)
    {
        channelRegistry.Add(new ChannelSession(
            0, match.ChatChannelName, $"MID {match.Id}'s multiplayer channel.",
            0, 0, false, "#multiplayer", true));
    }

    public async Task<MatchSession?> CreateAsync(PlayerSession host, ReadMatchResult data,
        CancellationToken cancellationToken = default, bool createdViaMakeCommand = false)
    {
        var match = matchRegistry.TryCreate(id =>
        {
            var created = BuildNew(id, data, host.Id, createdViaMakeCommand);
            RegisterChannel(created);
            return created;
        });

        if (match is null) return null;

        match.DbId = await matchPersistence.CreateMatchAsync(
            match.Name, DateTimeOffset.UtcNow.UtcDateTime, cancellationToken);

        await matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.Created,
            host.Id, host.Name, null, null, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);

        await match.Lock.WaitAsync(cancellationToken);
        try
        {
            Join(host, match, data.Password);
        }
        finally
        {
            match.Lock.Release();
        }

        return match;
    }

    /// <summary>
    ///     Backs the `api.` host's `POST /match` — creates a room with nobody in it (no chat "sender"
    ///     exists over HTTP, so there's no player to auto-join into slot 0 the way <see cref="CreateAsync" />
    ///     does for `!mp make`). <see cref="MatchSession.HostId" /> stays 0 and the referee list stays
    ///     empty until a caller assigns them via <c>PATCH /match/{id}/settings</c>/`host` and `addref`
    ///     actions. Marked <see cref="MatchSession.CreatedViaMakeCommand" /> for the same reason
    ///     `!mp make` rooms are — it should persist until explicitly closed, not auto-teardown the
    ///     moment a client briefly joins and leaves it.
    /// </summary>
    public async Task<MatchSession?> CreateEmptyAsync(ReadMatchResult data,
        CancellationToken cancellationToken = default)
    {
        var match = matchRegistry.TryCreate(id =>
        {
            var created = BuildNew(id, data, hostId: 0, createdViaMakeCommand: true);
            RegisterChannel(created);
            return created;
        });

        if (match is null) return null;

        match.DbId = await matchPersistence.CreateMatchAsync(
            match.Name, DateTimeOffset.UtcNow.UtcDateTime, cancellationToken);

        await matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.Created,
            null, null, null, null, DateTimeOffset.UtcNow.UtcDateTime, "Created via HTTP API"), cancellationToken);

        return match;
    }

    public bool Join(PlayerSession player, MatchSession match, string password)
    {
        if (player.Match is not null || match.TourneyClients.Contains(player.Id) ||
            match.BannedIds.Contains(player.Id) ||
            match.IsLocked)
        {
            player.Enqueue(ServerPacketWriter.MatchJoinFail());
            return false;
        }

        if (match.IsPrivate && (player.Priv & UserPrivileges.Staff) == 0 && !match.InvitedIds.Contains(player.Id))
        {
            player.Enqueue(ServerPacketWriter.MatchJoinFail());
            return false;
        }

        int slotId;
        if (player.Id != match.HostId)
        {
            if (password != match.Password && (player.Priv & UserPrivileges.Staff) == 0)
            {
                player.Enqueue(ServerPacketWriter.MatchJoinFail());
                return false;
            }

            var free = match.GetFreeSlotId();
            if (free is null)
            {
                player.Enqueue(ServerPacketWriter.MatchJoinFail());
                return false;
            }

            slotId = free.Value;
        }
        else
        {
            slotId = 0;
        }

        return OccupySlot(player, match, slotId);
    }

    /// <summary>
    ///     Server-initiated seating for a force-invite (<see cref="MatchControlService.ForceInvite" />) —
    ///     bypasses every join gate (password/private/locked/ban aren't re-checked here; the caller
    ///     already did). Fails only if the player is already in a match or the room is full.
    /// </summary>
    public bool ForceJoin(PlayerSession player, MatchSession match)
    {
        if (player.Match is not null) return false;

        var free = match.GetFreeSlotId();
        return free is not null && OccupySlot(player, match, free.Value);
    }

    /// <summary>
    ///     The slot-occupation tail shared by <see cref="Join" /> and <see cref="ForceJoin" />: channel
    ///     join, team default, slot fields, the <c>MatchJoinSuccess</c> packet, state broadcast, and the
    ///     <c>PlayerJoined</c> event.
    /// </summary>
    private bool OccupySlot(PlayerSession player, MatchSession match, int slotId)
    {
        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is null || !channelMembership.Join(player, channel)) return false;

        var lobby = channelRegistry.GetByName("#lobby");
        if (lobby is not null && player.InChannel(lobby.Name)) channelMembership.Part(player, lobby);

        var slot = match.Slots[slotId];
        if (match.TeamType is MatchTeamType.TeamVs or MatchTeamType.TagTeamVs) slot.Team = MatchTeam.Red;

        slot.Status = SlotStatus.NotReady;
        slot.PlayerId = player.Id;
        player.Match = match;

        player.Enqueue(ServerPacketWriter.MatchJoinSuccess(MatchPacketDataMapper.ToPacketData(match)));
        EnqueueState(match);

        _ = matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.PlayerJoined,
            player.Id, player.Name, null, null, DateTimeOffset.UtcNow.UtcDateTime, null));

        return true;
    }

    public void Leave(PlayerSession player, MatchSession match)
    {
        var slot = match.GetSlot(player.Id);
        if (slot is null)
        {
            player.Match = null;
            return;
        }

        slot.Reset(slot.Status == SlotStatus.Locked ? SlotStatus.Locked : SlotStatus.Open);

        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is not null) channelMembership.Part(player, channel);

        var hostTransfer = false;
        int? prevHostId = null;
        int? newHostId = null;

        if (match.Slots.All(s => s.Empty) && !match.CreatedViaMakeCommand)
        {
            TeardownMatch(match, channel);
        }
        else
        {
            if (player.Id == match.HostId)
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

            EnqueueState(match);
        }

        player.Match = null;

        _ = matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.PlayerLeft,
            player.Id, player.Name, null, null, DateTimeOffset.UtcNow.UtcDateTime, null));

        if (hostTransfer)
        {
            var prevHostName = prevHostId is not null
                ? sessionRegistry.GetById(prevHostId.Value)?.Name
                : null;
            var newHostName = newHostId is not null
                ? sessionRegistry.GetById(newHostId.Value)?.Name
                : null;
            _ = matchPersistence.CreateEventAsync(new MatchEventRow(
                match.DbId, (int)MatchEventType.HostGranted,
                prevHostId, prevHostName, newHostId, newHostName, DateTimeOffset.UtcNow.UtcDateTime, null));
        }
    }

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

        TeardownMatch(match, channel);

        await matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.Closed,
            actorId, actorName, null, null, DateTimeOffset.UtcNow.UtcDateTime, null), cancellationToken);
    }

    /// <summary>
    ///     Cancels a pending `!mp start &lt;seconds&gt;` countdown (not a plain `!mp timer`, which doesn't
    ///     start anything on its own) and announces why — called whenever a gameplay-affecting setting
    ///     (map, team type, win condition, size, a player's team) changes while a start is queued, since
    ///     starting the match under rules different from what was queued against would be misleading.
    /// </summary>
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

    private void TeardownMatch(MatchSession match, ChannelSession? channel)
    {
        match.PendingTimer?.Cancel();
        match.PendingTimer = null;

        matchRegistry.Remove(match.Id);
        if (channel is not null) channelRegistry.Remove(channel.Name);

        _ = matchPersistence.SetMatchEndedAsync(match.DbId, DateTimeOffset.UtcNow.UtcDateTime);

        var lobby = channelRegistry.GetByName("#lobby");
        if (lobby is not null)
            channelMembership.BroadcastToMembers(lobby, ServerPacketWriter.DisposeMatch(match.Id));
    }

    public async Task<bool> StartAsync(MatchSession match, CancellationToken cancellationToken = default)
    {
        var bmap = match.MapId > 0
            ? await mapRepo.FetchOneAsync(id: match.MapId, cancellationToken: cancellationToken)
            : null;

        if (match.MapId > 0 && bmap is null)
        {
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

        match.CurrentRoundId = await matchPersistence.CreateRoundAsync(
            match.DbId, match.NextRoundIndex++, match.MapId, match.MapMd5,
            match.Mode, match.WinCondition, match.TeamType,
            bmap?.Mapset.Artist ?? "", bmap?.Mapset.Title ?? "", bmap?.Version ?? "", bmap?.Mapset.Creator ?? "",
            match.Mods, DateTimeOffset.UtcNow.UtcDateTime, cancellationToken);

        Enqueue(match, ServerPacketWriter.MatchStart(MatchPacketDataMapper.ToPacketData(match)), false, noMap);
        EnqueueState(match);
        return true;
    }

    public void Enqueue(MatchSession match, byte[] data, bool lobby = true, IReadOnlyCollection<int>? immune = null)
    {
        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is not null) channelMembership.BroadcastToMembers(channel, data, immune);

        if (!match.IsPrivate)
            BroadcastToNonEmptyLobby(data, lobby);
    }

    public void EnqueueChat(MatchSession match, string senderName, int senderId, string text)
    {
        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is null) return;

        channelMembership.BroadcastPrivmsg(channel, IrcMessageWriter.Privmsg(senderName, senderId, channel.Name, text));
    }

    public void EnqueueState(MatchSession match, bool lobby = true)
    {
        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is not null)
            channelMembership.BroadcastToMembers(channel,
                ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match)));

        if (!match.IsPrivate)
            BroadcastToNonEmptyLobby(
                ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match), false), lobby);

        var mainSnapshot = MatchLiveSnapshotBuilder.BuildMain(match, sessionRegistry);
        eventBus.PublishMain(match.DbId, match.MainSnapshot.Publish(mainSnapshot));

        var settingsDelta = match.SettingsSnapshot.Publish(MatchLiveSnapshotBuilder.BuildSettings(match));
        eventBus.PublishSettings(match.DbId, settingsDelta);

        var liveDelta = match.LiveSnapshot.Publish(MatchLiveSnapshotBuilder.BuildLiveStatus(match));
        eventBus.PublishLive(match.DbId, liveDelta);

        for (var i = 0; i < match.SlotSnapshots.Count; i++)
        {
            var slotDelta = match.SlotSnapshots[i].Publish(mainSnapshot.Slots[i]);
            eventBus.PublishSlot(match.DbId, i, slotDelta);
        }
    }

    public void PublishHost(MatchSession match)
    {
        var delta = match.HostSnapshot.Publish(MatchLiveSnapshotBuilder.BuildHost(match, sessionRegistry));
        eventBus.PublishHost(match.DbId, delta);
    }

    public void PublishRefs(MatchSession match)
    {
        var delta = match.RefsSnapshot.Publish(MatchLiveSnapshotBuilder.BuildRefs(match, sessionRegistry));
        eventBus.PublishRefs(match.DbId, delta);
    }

    public void PublishBans(MatchSession match)
    {
        var delta = match.BansSnapshot.Publish(MatchLiveSnapshotBuilder.BuildBans(match, sessionRegistry));
        eventBus.PublishBans(match.DbId, delta);
    }

    public void PublishTimer(MatchSession match)
    {
        var delta = match.TimerSnapshot.Publish(MatchLiveSnapshotBuilder.BuildTimer(match));
        eventBus.PublishTimer(match.DbId, delta);
    }

    public void PublishSlots(MatchSession match)
    {
        var delta = match.SlotsSnapshot.Publish(MatchLiveSnapshotBuilder.BuildSlots(match, sessionRegistry));
        eventBus.PublishSlots(match.DbId, delta);
    }

    private void BroadcastToNonEmptyLobby(byte[] data, bool lobby)
    {
        if (!lobby) return;

        var lobbyChannel = channelRegistry.GetByName("#lobby");
        if (lobbyChannel is not null && lobbyChannel.PlayerCount > 0)
            channelMembership.BroadcastToMembers(lobbyChannel, data);
    }
}
