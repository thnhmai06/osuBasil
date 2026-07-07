using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Domain.Users;
using OpenOsuTournament.Bancho.Protocol.Packets;
using Action = OpenOsuTournament.Bancho.Domain.Action;

namespace OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;

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
            42, (int)Action.Idle, "", "", (int)Mods.NoMod, 0, 0, 900, 95.0, 10, 1000, 3, 0);
        Assert.Equal(expected, session.Dequeue());
    }
}