using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>Ported from app/api/domains/cho.py's ChannelPart (Player.leave_channel).</summary>
public sealed class ChannelPartHandler(IChannelRegistry channelRegistry, IPlayerSessionRegistry sessionRegistry)
    : IBanchoPacketHandler
{
    public ClientPackets PacketId => ClientPackets.ChannelPart;

    public bool AllowedWhenRestricted => true;

    public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
    {
        var name = reader.ReadString();

        if (name is "#highlight" or "#userlog") return Task.CompletedTask;

        var channel = channelRegistry.GetByName(name);
        if (channel is null || !player.InChannel(name)) return Task.CompletedTask;

        channel.Part(player.Id);
        player.LeaveChannel(name);

        foreach (var session in sessionRegistry.All)
            if (channel.CanRead(session.Priv))
                session.Enqueue(ServerPacketWriter.ChannelInfo(channel.Name, channel.Topic, channel.PlayerCount));

        return Task.CompletedTask;
    }
}