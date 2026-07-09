using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>
///     Ported from app/api/domains/cho.py's ChannelPart (Player.leave_channel). No kick packet is sent
///     back — the client already knows it left, since it's the one that sent this packet (unlike a
///     server-initiated part elsewhere, which does need one).
/// </summary>
public sealed class ChannelPartHandler(IChannelRegistry channelRegistry, ChannelMembershipService channelMembership)
    : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.ChannelPart;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var name = reader.ReadString();

        if (name is "#highlight" or "#userlog") return Task.CompletedTask;

        var channel = channelRegistry.GetByName(name);
        if (channel is not null) channelMembership.Part(player, channel, kick: false);

        return Task.CompletedTask;
    }
}
