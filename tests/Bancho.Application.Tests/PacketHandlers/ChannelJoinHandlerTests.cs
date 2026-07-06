using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using NSubstitute;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's ChannelJoin (calls Player.join_channel).</summary>
public class ChannelJoinHandlerTests
{
    private readonly IChannelRegistry _channelRegistry = Substitute.For<IChannelRegistry>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private ChannelJoinHandler MakeHandler() => new(_channelRegistry, _sessionRegistry);

    private static BanchoPacketReader ChannelNameReader(string name) => new(PacketWriter.WriteString(name));

    [Fact]
    public async Task Handle_ExistingReadableChannel_JoinsBothSidesAndSendsJoinSuccess()
    {
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);
        _channelRegistry.GetByName("#osu").Returns(channel);
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.All.Returns([player]);

        await MakeHandler().HandleAsync(player, ChannelNameReader("#osu"));

        Assert.True(channel.Contains(1));
        Assert.True(player.InChannel("#osu"));
        var expected = ServerPacketWriter.ChannelJoin("#osu")
            .Concat(ServerPacketWriter.ChannelInfo("#osu", "General", 1))
            .ToArray();
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
    public async Task Handle_NoReadPrivilege_NoOp()
    {
        var channel = new ChannelSession(1, "#staff", "Staff", (int)Privileges.Staff, (int)Privileges.Staff, autoJoin: true);
        _channelRegistry.GetByName("#staff").Returns(channel);
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        await MakeHandler().HandleAsync(player, ChannelNameReader("#staff"));

        Assert.False(channel.Contains(1));
        Assert.Empty(player.Dequeue());
    }

    [Fact]
    public async Task Handle_AlreadyJoined_NoOp()
    {
        var channel = new ChannelSession(1, "#osu", "General", 0, 0, autoJoin: true);
        _channelRegistry.GetByName("#osu").Returns(channel);
        var player = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        player.JoinChannel("#osu");
        channel.Join(player.Id);

        await MakeHandler().HandleAsync(player, ChannelNameReader("#osu"));

        Assert.Empty(player.Dequeue());
    }
}
