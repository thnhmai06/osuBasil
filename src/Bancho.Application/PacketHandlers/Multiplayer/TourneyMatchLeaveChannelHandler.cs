using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Application.Sessions.Channels;
using Bancho.Application.Sessions.Multiplayer;
using Bancho.Domain.Users;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Multiplayer;

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

        if (matchId is < 0 or >= 64 || (player.Priv & Privileges.Donator) == 0)
        {
            return Task.CompletedTask;
        }

        var match = matchRegistry.GetById(matchId);
        if (match is null || !match.TourneyClients.Contains(player.Id))
        {
            return Task.CompletedTask;
        }

        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is not null)
        {
            channelMembership.Part(player, channel);
        }

        match.RemoveTourneyClient(player.Id);
        return Task.CompletedTask;
    }
}
