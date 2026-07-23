using Basil.Application.PacketHandlers.Spectating;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's SpectateFrames — forwards raw bytes unparsed.</summary>
public class SpectateFramesHandlerTests
{
    private static PlayerSession MakePlayer(int id, string name)
    {
        return new PlayerSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task Handle_ForwardsRawBytesToAllSpectators()
    {
        var host = MakePlayer(1, "host");
        var spectator1 = MakePlayer(2, "alice");
        var spectator2 = MakePlayer(3, "bob");
        host.AddSpectator(spectator1);
        host.AddSpectator(spectator2);
        var rawFrameData = new byte[] { 1, 2, 3, 4, 5 };
        var playerInputEvents = new MultiplayerTestSupport.FakePlayerInputEvents();

        await new SpectateFramesHandler(playerInputEvents).HandleAsync(host, new BanchoPacketReader(rawFrameData));

        var expected = ServerPacketWriter.SpectateFrames(rawFrameData);
        Assert.Equal(expected, spectator1.Dequeue());
        Assert.Equal(expected, spectator2.Dequeue());
    }

    [Fact]
    public async Task Handle_NoSpectators_NoOp()
    {
        var host = MakePlayer(1, "host");
        var playerInputEvents = new MultiplayerTestSupport.FakePlayerInputEvents();

        await new SpectateFramesHandler(playerInputEvents).HandleAsync(host, new BanchoPacketReader(new byte[] { 1 }));

        Assert.Empty(host.Dequeue());
    }

    [Fact]
    public async Task Handle_SpectatedPlayerNotInAMatch_StillPublishesInputFrameByPlayerId()
    {
        var host = MakePlayer(1, "host");
        var playerInputEvents = new MultiplayerTestSupport.FakePlayerInputEvents();

        await new SpectateFramesHandler(playerInputEvents).HandleAsync(host, new BanchoPacketReader(new byte[] { 1 }));

        var publish = Assert.Single(playerInputEvents.Publishes);
        Assert.Equal(host.Id, publish.PlayerId);
    }

    [Fact]
    public async Task Handle_SpectatedPlayerInAMatch_AlsoPublishesInputFrameByPlayerId()
    {
        var host = MakePlayer(1, "host");
        var fixture = new MultiplayerTestSupport.Fixture();
        fixture.RegisterAll(host);
        fixture.MatchMembership.CreateAsync(host, MultiplayerTestSupport.MakeMatchData(host.Id))
            .GetAwaiter().GetResult();
        var playerInputEvents = new MultiplayerTestSupport.FakePlayerInputEvents();

        await new SpectateFramesHandler(playerInputEvents).HandleAsync(host, new BanchoPacketReader(new byte[] { 9 }));

        var publish = Assert.Single(playerInputEvents.Publishes);
        Assert.Equal(host.Id, publish.PlayerId);
    }
}
