using System.Text.Json;
using Basil.Application.Abstractions;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;

namespace Basil.Application.UseCases.Multiplayer;

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
    IMatchEventBus eventBus,
    IClock clock,
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
            (MatchWinConditions)data.WinCondition,
            (MatchTeamTypes)data.TeamType,
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
            match.Name, clock.UtcNow.UtcDateTime, cancellationToken);

        await matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.Created,
            host.Id, host.Name, null, null, clock.UtcNow.UtcDateTime, null), cancellationToken);

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

    public bool Join(PlayerSession player, MatchSession match, string password)
    {
        if (player.Match is not null || match.TourneyClients.Contains(player.Id) ||
            match.BannedIds.Contains(player.Id) ||
            match.IsLocked)
        {
            player.Enqueue(ServerPacketWriter.MatchJoinFail());
            return false;
        }

        int slotId;
        if (player.Id != match.HostId)
        {
            if (password != match.Password && (player.Priv & Privileges.Staff) == 0)
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

        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is null || !channelMembership.Join(player, channel)) return false;

        var lobby = channelRegistry.GetByName("#lobby");
        if (lobby is not null && player.InChannel(lobby.Name)) channelMembership.Part(player, lobby);

        var slot = match.Slots[slotId];
        if (match.TeamType is MatchTeamTypes.TeamVs or MatchTeamTypes.TagTeamVs) slot.Team = MatchTeams.Red;

        slot.Status = SlotStatus.NotReady;
        slot.PlayerId = player.Id;
        player.Match = match;

        player.Enqueue(ServerPacketWriter.MatchJoinSuccess(MatchPacketDataMapper.ToPacketData(match)));
        EnqueueState(match);

        _ = matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.PlayerJoined,
            player.Id, player.Name, null, null, clock.UtcNow.UtcDateTime, null));

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
            player.Id, player.Name, null, null, clock.UtcNow.UtcDateTime, null));

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
                prevHostId, prevHostName, newHostId, newHostName, clock.UtcNow.UtcDateTime, null));
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

        await matchPersistence.SetMatchEndedAsync(match.DbId, clock.UtcNow.UtcDateTime, cancellationToken);

        await matchPersistence.CreateEventAsync(new MatchEventRow(
            match.DbId, (int)MatchEventType.Closed,
            actorId, actorName, null, null, clock.UtcNow.UtcDateTime, null), cancellationToken);
    }

    private void TeardownMatch(MatchSession match, ChannelSession? channel)
    {
        match.PendingTimer?.Cancel();
        match.PendingTimer = null;

        matchRegistry.Remove(match.Id);
        if (channel is not null) channelRegistry.Remove(channel.Name);

        var lobby = channelRegistry.GetByName("#lobby");
        if (lobby is not null)
            channelMembership.BroadcastToMembers(lobby, ServerPacketWriter.DisposeMatch(match.Id));
    }

    public async Task StartAsync(MatchSession match, CancellationToken cancellationToken = default)
    {
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

        var bmap = match.MapId > 0
            ? await mapRepo.FetchOneAsync(id: match.MapId, cancellationToken: cancellationToken)
            : null;

        match.CurrentRoundId = await matchPersistence.CreateRoundAsync(
            match.DbId, match.NextRoundIndex++, match.MapId, match.MapMd5,
            (int)match.Mode, (int)match.WinCondition, (int)match.TeamType,
            bmap?.Artist ?? "", bmap?.Title ?? "", bmap?.Version ?? "", bmap?.Creator ?? "",
            (int)match.Mods, clock.UtcNow.UtcDateTime, cancellationToken);

        Enqueue(match, ServerPacketWriter.MatchStart(MatchPacketDataMapper.ToPacketData(match)), false, noMap);
        EnqueueState(match);
    }

    public void Enqueue(MatchSession match, byte[] data, bool lobby = true, IReadOnlyCollection<int>? immune = null)
    {
        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is not null) channelMembership.BroadcastToMembers(channel, data, immune);

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

        BroadcastToNonEmptyLobby(ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match), false),
            lobby);

        eventBus.PublishMain(match.DbId,
            JsonSerializer.SerializeToUtf8Bytes(MatchLiveSnapshotBuilder.BuildMain(match, sessionRegistry)));
    }

    private void BroadcastToNonEmptyLobby(byte[] data, bool lobby)
    {
        if (!lobby) return;

        var lobbyChannel = channelRegistry.GetByName("#lobby");
        if (lobbyChannel is not null && lobbyChannel.PlayerCount > 0)
            channelMembership.BroadcastToMembers(lobbyChannel, data);
    }
}
