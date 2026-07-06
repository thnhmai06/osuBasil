using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's SpectateFrames — forwards raw bytes unparsed.</summary>
public class SpectateFramesHandlerTests
{
    private static PlayerSession MakePlayer(int id, string name) => new(id, name, "token", Privileges.Unrestricted, 0.0);

    [Fact]
    public async Task Handle_ForwardsRawBytesToAllSpectators()
    {
        var host = MakePlayer(1, "host");
        var spectator1 = MakePlayer(2, "alice");
        var spectator2 = MakePlayer(3, "bob");
        host.AddSpectator(spectator1);
        host.AddSpectator(spectator2);
        var rawFrameData = new byte[] { 1, 2, 3, 4, 5 };

        await new SpectateFramesHandler().HandleAsync(host, new BanchoPacketReader(rawFrameData));

        var expected = ServerPacketWriter.SpectateFrames(rawFrameData);
        Assert.Equal(expected, spectator1.Dequeue());
        Assert.Equal(expected, spectator2.Dequeue());
    }

    [Fact]
    public async Task Handle_NoSpectators_NoOp()
    {
        var host = MakePlayer(1, "host");

        await new SpectateFramesHandler().HandleAsync(host, new BanchoPacketReader(new byte[] { 1 }));

        Assert.Empty(host.Dequeue());
    }
}
