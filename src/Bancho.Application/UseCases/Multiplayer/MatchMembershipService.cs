using Bancho.Application.Commands;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.UseCases.Multiplayer;

/// <summary>
/// Ported from Player.join_match/leave_match plus Match.enqueue/enqueue_state. Every method here
/// that reads-then-mutates a match's slots or settings must be called with <see cref="MatchSession.Lock"/>
/// already held by the caller (packet handlers own the lock's lifetime, since the lock also has
/// to span the eventual `enqueue_state` broadcast — see MatchSession's doc comment).
/// </summary>
public sealed class MatchMembershipService(
    IMatchRegistry matchRegistry,
    IChannelRegistry channelRegistry,
    IPlayerSessionRegistry sessionRegistry,
    ChannelMembershipService channelMembership)
{
    private const string PrivateSuffix = "//private";
    private const int MaxMatchNameLength = 50; // matches MAX_MATCH_NAME_LENGTH in app/objects/match.py

    /// <summary>Ported from cho.py's validate_match_data — shared by CreateMatch/MatchChangeSettings/MatchChangePassword.</summary>
    public static bool ValidateMatchData(ReadMatchResult data, int expectedHostId) =>
        data.HostId == expectedHostId && data.Name.Length <= MaxMatchNameLength;

    public static string ChannelNameFor(int matchId) => $"#multi_{matchId}";

    /// <summary>Ported from the MatchCreate handler's Match/Channel construction, given a registry-assigned id.</summary>
    public static MatchSession BuildNew(int id, ReadMatchResult data, int hostId)
    {
        var isPrivate = data.Password.EndsWith(PrivateSuffix, StringComparison.Ordinal);
        return new MatchSession(
            id: id,
            name: data.Name,
            password: isPrivate ? data.Password[..^PrivateSuffix.Length] : data.Password,
            hasPublicHistory: !isPrivate,
            mapName: data.MapName,
            mapId: data.MapId,
            mapMd5: data.MapMd5,
            hostId: hostId,
            mode: (GameMode)data.Mode,
            mods: (Mods)data.Mods,
            winCondition: (MatchWinConditions)data.WinCondition,
            teamType: (MatchTeamTypes)data.TeamType,
            freemods: data.FreeMods,
            seed: data.Seed,
            chatChannelName: ChannelNameFor(id));
    }

    /// <summary>Creates the `#multi_{id}` instance channel for a newly-registered match. Must run before <see cref="Join"/>.</summary>
    public void RegisterChannel(MatchSession match)
    {
        channelRegistry.Add(new ChannelSession(
            id: 0, name: match.ChatChannelName, topic: $"MID {match.Id}'s multiplayer channel.",
            readPriv: 0, writePriv: 0, autoJoin: false, displayName: "#multiplayer", instance: true));
    }

    /// <summary>
    /// Ported from the MatchCreate handler: atomically allocates a registry slot, builds the
    /// match + its channel, and joins the host into slot 0. Returns null if the 64-slot table is
    /// full (caller sends match_join_fail, matching MatchCreate.handle).
    /// </summary>
    public MatchSession? Create(PlayerSession host, ReadMatchResult data)
    {
        var match = matchRegistry.TryCreate(id =>
        {
            var created = BuildNew(id, data, host.Id);
            RegisterChannel(created);
            return created;
        });

        if (match is null)
        {
            return null;
        }

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
    /// Ported from Player.join_match. Caller must already hold <paramref name="match"/>'s Lock —
    /// this covers both the free-slot race (join concurrent with another join/part) and the
    /// subsequent broadcast, matching bancho.py's asyncio-given atomicity for the same sequence.
    /// </summary>
    public bool Join(PlayerSession player, MatchSession match, string password)
    {
        if (player.Match is not null || match.TourneyClients.Contains(player.Id))
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
        if (channel is null || !channelMembership.Join(player, channel))
        {
            return false;
        }

        var lobby = channelRegistry.GetByName("#lobby");
        if (lobby is not null && player.InChannel(lobby.Name))
        {
            channelMembership.Part(player, lobby);
        }

        var slot = match.Slots[slotId];
        if (match.TeamType is MatchTeamTypes.TeamVs or MatchTeamTypes.TagTeamVs)
        {
            slot.Team = MatchTeams.Red;
        }

        slot.Status = SlotStatus.NotReady;
        slot.PlayerId = player.Id;
        player.Match = match;

        player.Enqueue(ServerPacketWriter.MatchJoinSuccess(MatchPacketDataMapper.ToPacketData(match)));
        EnqueueState(match);
        return true;
    }

    /// <summary>
    /// Ported from Player.leave_match. Caller must already hold <paramref name="match"/>'s Lock —
    /// the "is the match now empty" check-then-act (remove from registry) must happen inside the
    /// same critical section as the slot reset that could make it true.
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
        if (channel is not null)
        {
            channelMembership.Part(player, channel);
        }

        if (match.Slots.All(s => s.Empty))
        {
            matchRegistry.Remove(match.Id);
            if (channel is not null)
            {
                channelRegistry.Remove(channel.Name);
            }

            var lobby = channelRegistry.GetByName("#lobby");
            if (lobby is not null)
            {
                channelMembership.BroadcastToMembers(lobby, ServerPacketWriter.DisposeMatch(match.Id));
            }
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

            match.RemoveReferee(player.Id);
            EnqueueState(match);
        }

        player.Match = null;
    }

    /// <summary>
    /// Ported from Match.start. Caller must already hold <paramref name="match"/>'s Lock — shared
    /// by MATCH_START and !mp start/!mp force-start, matching how both call the same Python method.
    /// </summary>
    public void Start(MatchSession match)
    {
        var noMap = new List<int>();
        foreach (var slot in match.Slots)
        {
            if (slot.PlayerId is not null)
            {
                if (slot.Status != SlotStatus.NoMap)
                {
                    slot.Status = SlotStatus.Playing;
                }
                else
                {
                    noMap.Add(slot.PlayerId.Value);
                }
            }
        }

        match.InProgress = true;
        Enqueue(match, ServerPacketWriter.MatchStart(MatchPacketDataMapper.ToPacketData(match)), lobby: false, immune: noMap);
        EnqueueState(match);
    }

    /// <summary>Ported from Match.enqueue.</summary>
    public void Enqueue(MatchSession match, byte[] data, bool lobby = true, IReadOnlyCollection<int>? immune = null)
    {
        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is not null)
        {
            channelMembership.BroadcastToMembers(channel, data, immune);
        }

        BroadcastToNonEmptyLobby(data, lobby);
    }

    /// <summary>Ported from Match.enqueue_state.</summary>
    public void EnqueueState(MatchSession match, bool lobby = true)
    {
        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is not null)
        {
            channelMembership.BroadcastToMembers(channel, ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match), sendPassword: true));
        }

        BroadcastToNonEmptyLobby(ServerPacketWriter.UpdateMatch(MatchPacketDataMapper.ToPacketData(match), sendPassword: false), lobby);
    }

    /// <summary>
    /// Ported from Channel.send_bot, called via match.chat.send_bot — unlike <see cref="Enqueue"/>,
    /// this never mirrors to `#lobby`; it's a message in the match's own chat channel only.
    /// </summary>
    public void SendBot(MatchSession match, string message)
    {
        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is null)
        {
            return;
        }

        const string botName = "BanchoBot";
        channelMembership.BroadcastToMembers(channel, ServerPacketWriter.SendMessage(botName, message, channel.DisplayName, CommandTargetResolver.BotId));
    }

    private void BroadcastToNonEmptyLobby(byte[] data, bool lobby)
    {
        if (!lobby)
        {
            return;
        }

        var lobbyChannel = channelRegistry.GetByName("#lobby");
        if (lobbyChannel is not null && lobbyChannel.PlayerCount > 0)
        {
            channelMembership.BroadcastToMembers(lobbyChannel, data);
        }
    }
}
