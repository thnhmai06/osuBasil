using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Domain.Users;
using Bancho.Protocol.Packets;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's StatsUpdateRequest (@register(ClientPackets.REQUEST_STATUS_UPDATE)).</summary>
public class RequestStatusUpdateHandlerTests
{
    [Fact]
    public async Task Handle_EnqueuesOwnUserStatsPacket()
    {
        var session = new PlayerSession(42, "cmyui", "token", Privileges.Unrestricted, 0.0);
        session.ModeStats[0] = new CachedPlayerStats(1000, 900, 95.0, 10, 500, 200, 300, 3);
        var reader = new BanchoPacketReader(Array.Empty<byte>());

        await new RequestStatusUpdateHandler().HandleAsync(session, reader);

        var expected = ServerPacketWriter.UserStats(
            42, (int)Domain.Action.Idle, "", "", (int)Mods.NoMod, 0, 0, 900, 95.0, 10, 1000, 3, 0);
        Assert.Equal(expected, session.Dequeue());
    }
}
