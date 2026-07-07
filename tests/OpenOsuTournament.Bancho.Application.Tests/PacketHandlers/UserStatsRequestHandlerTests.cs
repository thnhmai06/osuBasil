using NSubstitute;
using OpenOsuTournament.Bancho.Application.PacketHandlers.Core;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Users;
using OpenOsuTournament.Bancho.Protocol.Packets;
using Action = OpenOsuTournament.Bancho.Domain.Action;

namespace OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's StatsRequest (@register(ClientPackets.USER_STATS_REQUEST)).</summary>
public class UserStatsRequestHandlerTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    [Fact]
    public async Task Handle_UnrestrictedTarget_EnqueuesTheirStats()
    {
        var self = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "target", "target-token", Privileges.Unrestricted, 0.0);
        target.ModeStats[GameMode.VanillaOsu] = new CachedPlayerStats(1000, 900, 95.0, 10, 500, 200, 300, 3);
        _sessionRegistry.All.Returns([self, target]);
        _sessionRegistry.GetById(2).Returns(target);
        var reader = new BanchoPacketReader(PacketWriter.WriteI32List([2]));

        await new UserStatsRequestHandler(_sessionRegistry).HandleAsync(self, reader);

        var expected = ServerPacketWriter.UserStats(2, (int)Action.Idle, "", "", (int)Mods.NoMod, 0, 0, 900, 95.0, 10,
            1000, 3, 0);
        Assert.Equal(expected, self.Dequeue());
    }

    [Fact]
    public async Task Handle_RestrictedTarget_NotEnqueued()
    {
        var self = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var target = new PlayerSession(2, "target", "target-token", Privileges.Verified, 0.0); // restricted
        _sessionRegistry.All.Returns([self, target]);
        var reader = new BanchoPacketReader(PacketWriter.WriteI32List([2]));

        await new UserStatsRequestHandler(_sessionRegistry).HandleAsync(self, reader);

        Assert.Empty(self.Dequeue());
    }

    [Fact]
    public async Task Handle_OwnId_ExcludedFromResults()
    {
        var self = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        _sessionRegistry.All.Returns([self]);
        var reader = new BanchoPacketReader(PacketWriter.WriteI32List([1]));

        await new UserStatsRequestHandler(_sessionRegistry).HandleAsync(self, reader);

        Assert.Empty(self.Dequeue());
    }
}