using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Domain.Users;
using Bancho.Protocol.Packets;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's Ping (@register(ClientPackets.PING, restricted=True)) — a no-op.</summary>
public class PingHandlerTests
{
    [Fact]
    public void PacketId_IsPing() => Assert.Equal(ClientPackets.Ping, new PingHandler().PacketId);

    [Fact]
    public void AllowedWhenRestricted_IsTrue() => Assert.True(new PingHandler().AllowedWhenRestricted);

    [Fact]
    public async Task Handle_DoesNothing()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var reader = new BanchoPacketReader(Array.Empty<byte>());

        await new PingHandler().HandleAsync(session, reader);

        Assert.Empty(session.Dequeue());
    }
}
