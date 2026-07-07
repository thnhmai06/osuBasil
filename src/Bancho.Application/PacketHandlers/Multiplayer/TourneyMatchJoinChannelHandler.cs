using Bancho.Application.PacketHandlers.Core;
using Bancho.Application.Sessions;
using Bancho.Application.Sessions.Channels;
using Bancho.Application.Sessions.Multiplayer;
using Bancho.Domain.Users;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Multiplayer;

/// <summary>Ported from app/api/domains/cho.py's TourneyMatchJoinChannel.</summary>
public sealed class TourneyMatchJoinChannelHandler(
    IMatchRegistry matchRegistry,
    IChannelRegistry channelRegistry,
    ChannelMembershipService channelMembership) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.TournamentJoinMatchChannel;

    public bool AllowedWhenRestricted => false;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var matchId = reader.ReadI32();

        if (matchId is < 0 or >= 64 || (player.Priv & Privileges.Donator) == 0) return Task.CompletedTask;

        var match = matchRegistry.GetById(matchId);
        if (match is null) return Task.CompletedTask;

        if (match.Slots.Any(s => s.PlayerId == player.Id)) return Task.CompletedTask; // already playing in the match

        var channel = channelRegistry.GetByName(match.ChatChannelName);
        if (channel is not null && channelMembership.Join(player, channel)) match.AddTourneyClient(player.Id);

        return Task.CompletedTask;
    }
}