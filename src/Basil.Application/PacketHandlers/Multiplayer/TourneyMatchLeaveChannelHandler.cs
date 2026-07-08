using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchLeaveChannel.</summary>
public sealed class TourneyMatchLeaveChannelHandler(
    IMatchRegistry matchRegistry,
    IChannelRegistry channelRegistry,
    ChannelMembershipService channelMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.TournamentLeaveMatchChannel;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var matchId = reader.ReadI32();

        if (matchId is < 0 or >= 64 || (player.Priv & Privileges.Donator) == 0) return Task.CompletedTask;

        var match = matchRegistry.GetById(matchId);
        if (match is null || !match.TourneyClients.Contains(player.Id)) return Task.CompletedTask;

        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is not null) channelMembership.Part(player, channel);

        match.RemoveTourneyClient(player.Id);
        return Task.CompletedTask;
    }
}