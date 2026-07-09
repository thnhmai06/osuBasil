using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>
///     Ported from app/api/domains/cho.py's ChannelJoin, which delegates to Player.join_channel:
///     updates membership on both sides (Channel.append + Player.channels.append) then broadcasts
///     the updated channel_info to every session that can read it. Login only sends channel_info
///     for auto-join channels — the client is expected to send this packet itself to actually join.
///     Membership/broadcast logic itself lives in <see cref="ChannelMembershipService" /> — shared with
///     server-initiated joins (spectator, multiplayer) and real IRC connections.
/// </summary>
public sealed class ChannelJoinHandler(IChannelRegistry channelRegistry, ChannelMembershipService channelMembership)
    : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.ChannelJoin;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var name = reader.ReadString();

        if (name is "#highlight" or "#userlog") return Task.CompletedTask;

        var channel = channelRegistry.GetByName(name);
        if (channel is not null) channelMembership.Join(player, channel);

        return Task.CompletedTask;
    }
}
