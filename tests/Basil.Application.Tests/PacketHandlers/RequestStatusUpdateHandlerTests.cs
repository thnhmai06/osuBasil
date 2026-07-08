using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Domain;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using Action = Basil.Domain.Action;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's StatsUpdateRequest (@register(ClientPackets.REQUEST_STATUS_UPDATE)).</summary>
public class RequestStatusUpdateHandlerTests
{
    [Fact]
    public async Task Handle_EnqueuesOwnUserStatsPacket()
    {
        var session = new PlayerSession(42, "cmyui", "token", Privileges.Unrestricted, 0.0);
        session.ModeStats[GameMode.VanillaOsu] = new CachedPlayerStats(1000, 900, 95.0, 10, 500, 200, 300, 3);
        var reader = new BanchoPacketReader(Array.Empty<byte>());

        await new RequestStatusUpdateHandler().HandleAsync(session, reader);

        var expected = ServerPacketWriter.UserStats(
            42, (int)Action.Idle, "", "", (int)Mods.NoMod, 0, 0, 900, 95.0, 10, 1000, 3, 0);
        Assert.Equal(expected, session.Dequeue());
    }
}