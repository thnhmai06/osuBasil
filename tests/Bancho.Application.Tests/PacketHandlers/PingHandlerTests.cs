using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's Ping (@register(ClientPackets.PING, restricted=True)) — a no-op.</summary>
public class PingHandlerTests
{
    [Fact]
    public void PacketId_IsPing() => Assert.Equal(ClientPackets.Ping, new PingHandler().PacketId);

    [Fact]
    public void AllowedWhenRestricted_IsTrue() => Assert.True(new PingHandler().AllowedWhenRestricted);

    [Fact]
    public void Handle_DoesNothing()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var reader = new BanchoPacketReader(Array.Empty<byte>());

        new PingHandler().Handle(session, reader);

        Assert.Empty(session.Dequeue());
    }
}
