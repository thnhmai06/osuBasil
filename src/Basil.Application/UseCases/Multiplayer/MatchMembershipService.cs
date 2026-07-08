using System.Text.Json;
using Basil.Application.Abstractions;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
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
    IClock clock)
{
    private const int MaxMatchNameLength = 50; // matches MAX_MATCH_NAME_LENGTH in app/objects/match.py

    /// <summary>Ported from cho.py's validate_match_data — shared by CreateMatch/MatchChangeSettings/MatchChangePassword.</summary>
    public static bool ValidateMatchData(ReadMatchResult data, int expectedHostId)
    {
        return data.HostId == expectedHostId && data.Name.Length <= MaxMatchNameLength;
    }

    public static string ChannelNameFor(int matchId)
    {
        return $"#multi_{matchId}";
    }

    /// <summary>Ported from the MatchCreate handler's Match/Channel construction, given a registry-assigned id.</summary>
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

    /// <summary>Creates the `#multi_{id}` instance channel for a newly-registered match. Must run before <see cref="Join" />.</summary>
    public void RegisterChannel(MatchSession match)
    {
        channelRegistry.Add(new ChannelSession(
            0, match.ChatChannelName, $"MID {match.Id}'s multiplayer channel.",
            0, 0, false, "#multiplayer", true));
    }

    /// <summary>
    ///     Ported from the MatchCreate handler: atomically allocates a registry slot, builds the
    ///     match + its channel, and joins the host into slot 0. Returns null if the 64-slot table is
    ///     full (caller sends match_join_fail, matching MatchCreate.handle). Also persists a Matches
    ///     row — <see cref="MatchSession.DbId" /> is the stable id external consumers (TRT/WS) use,
    ///     distinct from the 0-63 in-memory <see cref="MatchSession.Id" /> the wire protocol uses.
    /// </summary>
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
            match.Name, (int)match.Mode, (int)match.WinCondition, (int)match.TeamType, match.HostId,
            clock.UtcNow.UtcDateTime, cancellationToken);

        match.Lock.Wait();
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
    ///     Ported from Player.join_match. Caller must already hold <paramref name="match" />'s Lock —
    ///     this covers both the free-slot race (join concurrent with another join/part) and the
    ///     subsequent broadcast, matching bancho.py's asyncio-given atomicity for the same sequence.
    /// </summary>
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
        return true;
    }

    /// <summary>
    ///     Ported from Player.leave_match. Caller must already hold <paramref name="match" />'s Lock —
    ///     the "is the match now empty" check-then-act (remove from registry) must happen inside the
    ///     same critical section as the slot reset that could make it true. A room created via
    ///     `!mp make` (<see cref="MatchSession.CreatedViaMakeCommand" />) never auto-tears-down here
    ///     regardless of occupancy — it only closes via `!mp close` or once its referee list empties
    ///     (see MpCommandService.RemoveRefereeAsync) — matching normal client-created rooms' existing
    ///     empty-triggers-teardown behavior otherwise. Referee status is never touched by leaving —
    ///     a referee doesn't need to be physically present in the room.
    /// </summary>
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

        if (match.Slots.All(s => s.Empty) && !match.CreatedViaMakeCommand)
        {
            TeardownMatch(match, channel);
        }
        else
        {
            if (player.Id == match.HostId)
            {
                var newHostSlot = match.Slots.FirstOrDefault(s => !s.Empty);
                if (newHostSlot is not null)
                {
                    match.HostId = newHostSlot.PlayerId!.Value;
                    sessionRegistry.GetById(match.HostId)?.Enqueue(ServerPacketWriter.MatchTransferHost());
                }
            }

            EnqueueState(match);
        }

        player.Match = null;
    }

    /// <summary>
    ///     Backs `!mp close` — tears the match down immediately regardless of occupancy, unlike
    ///     <see cref="Leave" /> which only tears down once every slot empties naturally. Caller must
    ///     already hold <paramref name="match" />'s Lock, same as every other slot-mutating method here.
    ///     Skips per-player host-reassignment/referee cleanup that <see cref="Leave" /> does — pointless
    ///     work when the whole room is being destroyed. Also closes the gap where nothing previously
    ///     called <see cref="IMatchPersistenceRepository.SetMatchEndedAsync" /> for any match.
    /// </summary>
    public async Task CloseAsync(MatchSession match, CancellationToken cancellationToken = default)
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
    }

    /// <summary>
    ///     Shared by <see cref="Leave" /> (once every slot empties naturally) and <see cref="CloseAsync" />
    ///     (immediate, regardless of occupancy): removes the match from the registry, removes its chat
    ///     channel, and tells `#lobby` it's gone.
    /// </summary>
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

    /// <summary>
    ///     Ported from Match.start. Caller must already hold <paramref name="match" />'s Lock — shared
    ///     by MATCH_START and !mp start/!mp force-start, matching how both call the same Python method.
    ///     Also opens a new Round row for the beatmap about to be played — see
    ///     <see cref="MatchSession.CurrentRoundId" />'s doc comment for why score submission links to
    ///     it directly instead of MatchComplete gathering scores after the fact.
    /// </summary>
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

        match.CurrentRoundId = await matchPersistence.CreateRoundAsync(
            match.DbId, match.NextRoundIndex++, match.MapId, match.MapMd5, (int)match.Mods,
            clock.UtcNow.UtcDateTime, cancellationToken);

        Enqueue(match, ServerPacketWriter.MatchStart(MatchPacketDataMapper.ToPacketData(match)), false, noMap);
        EnqueueState(match);
    }

    /// <summary>Ported from Match.enqueue.</summary>
    public void Enqueue(MatchSession match, byte[] data, bool lobby = true, IReadOnlyCollection<int>? immune = null)
    {
        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is not null) channelMembership.BroadcastToMembers(channel, data, immune);

        BroadcastToNonEmptyLobby(data, lobby);
    }

    /// <summary>Ported from Match.enqueue_state.</summary>
    public void EnqueueState(MatchSession match, bool lobby = true)
    {
        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is not null)
            channelMembership.BroadcastToMembers(channel,
                ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match)));

        BroadcastToNonEmptyLobby(ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match), false),
            lobby);

        // New for the api. host's live WS layer — every state-changing packet routes through here,
        // so this is the single choke point for the WS /multi/{id} "main" channel. Publishing is a
        // non-blocking best-effort write (see IMatchEventBus's doc comment), safe to call while
        // still holding match.Lock.
        eventBus.PublishMain(match.DbId,
            JsonSerializer.SerializeToUtf8Bytes(MatchLiveSnapshotBuilder.BuildMain(match)));
    }

    private void BroadcastToNonEmptyLobby(byte[] data, bool lobby)
    {
        if (!lobby) return;

        var lobbyChannel = channelRegistry.GetByName("#lobby");
        if (lobbyChannel is not null && lobbyChannel.PlayerCount > 0)
            channelMembership.BroadcastToMembers(lobbyChannel, data);
    }
}