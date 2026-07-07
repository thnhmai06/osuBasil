using Bancho.Application.PacketHandlers.Channels;
using Bancho.Application.Sessions;
using Bancho.Application.Sessions.Channels;
using Bancho.Domain.Users;
using Bancho.Protocol.Packets;
using NSubstitute;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's ChannelPart (calls Player.leave_channel).</summary>
public class ChannelPartHandlerTests
{
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private ChannelPartHandler MakeHandler()
    {
        return new ChannelPartHandler(_channelRegistry, _sessionRegistry);
    }

    private static BanchoPacketReader ChannelNameReader(string name)
    {
        return new BanchoPacketReader(PacketWriter.WriteString(name));
    }

    [Fact]
    public async Task Handle_JoinedChannel_LeavesBothSidesAndBroadcastsUpdatedInfo()
    {
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        channel.Join(player.Id);
        player.JoinChannel("#osu");
        _channelRegistry.GetByName("#osu").Returns(channel);
        _sessionRegistry.All.Returns([player]);

        await MakeHandler().HandleAsync(player, ChannelNameReader("#osu"));

        Assert.False(channel.Contains(1));
        Assert.False(player.InChannel("#osu"));
        var expected = ServerPacketWriter.ChannelInfo("#osu", "General", 0);
        Assert.Equal(expected, player.Dequeue());
    }

    [Fact]
    public async Task Handle_UnknownChannel_NoOp()
    {
        _channelRegistry.GetByName("#missing").Returns((ChannelSession?)null);
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        await MakeHandler().HandleAsync(player, ChannelNameReader("#missing"));

        Assert.Empty(player.Dequeue());
    }

    [Fact]
    public async Task Handle_NotJoined_NoOp()
    {
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, true);
        _channelRegistry.GetByName("#osu").Returns(channel);
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        await MakeHandler().HandleAsync(player, ChannelNameReader("#osu"));

        Assert.Empty(player.Dequeue());
    }
}