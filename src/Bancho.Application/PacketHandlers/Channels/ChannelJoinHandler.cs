using Bancho.Application.Sessions;
using Bancho.Protocol;
using Bancho.Application.Abstractions.Channels;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Application.Sessions.Channels;
using Bancho.Protocol.Packets;

namespace Bancho.Application.PacketHandlers.Channels;

/// <summary>
/// Ported from app/api/domains/cho.py's ChannelJoin, which delegates to Player.join_channel:
/// updates membership on both sides (Channel.append + Player.channels.append) then broadcasts
/// the updated channel_info to every session that can read it. Login only sends channel_info
/// for auto-join channels — the client is expected to send this packet itself to actually join.
/// </summary>
public sealed class ChannelJoinHandler(IChannelRegistry channelRegistry, IPlayerSessionRegistry sessionRegistry) : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.ChannelJoin;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var name = reader.ReadString();

        if (name is "#highlight" or "#userlog")
        {
            return Task.CompletedTask;
        }

        var channel = channelRegistry.GetByName(name);
        if (channel is null || !channel.CanRead(player.Priv) || player.InChannel(name))
        {
            return Task.CompletedTask;
        }

        channel.Join(player.Id);
        player.JoinChannel(name);

        player.Enqueue(ServerPacketWriter.ChannelJoin(name));

        foreach (var session in sessionRegistry.All)
        {
            if (channel.CanRead(session.Priv))
            {
                session.Enqueue(ServerPacketWriter.ChannelInfo(channel.Name, channel.Topic, channel.PlayerCount));
            }
        }

        return Task.CompletedTask;
    }
}
