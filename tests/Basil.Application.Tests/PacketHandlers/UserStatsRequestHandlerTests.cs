using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>Ported from app/api/domains/cho.py's StatsRequest (@register(ClientPackets.USER_STATS_REQUEST)).</summary>
public class UserStatsRequestHandlerTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    [Fact]
    public async Task Handle_UnrestrictedTarget_EnqueuesTheirStats()
    {
        var self = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        var target = new PlayerSession(2, "target", "target-token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        target.ModeStats[GameMode.Standard] = new CachedPlayerStats(1000, 900, 95.0, 10, 3);
        _sessionRegistry.All.Returns([self, target]);
        _sessionRegistry.GetById(2).Returns(target);
        var reader = new BanchoPacketReader(PacketWriter.WriteI32List([2]));

        await new UserStatsRequestHandler(_sessionRegistry).HandleAsync(self, reader);

        var expected = ServerPacketWriter.UserStats(2, (int)UserActivity.Idle, "", "", (int)Mods.NoMod, 0, 0, 900, 95.0, 10,
            1000, 3, 0);
        Assert.Equal(expected, self.Dequeue());
    }

    [Fact]
    public async Task Handle_RestrictedTarget_NotEnqueued()
    {
        var self = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        var target = new PlayerSession(2, "target", "target-token", UserPrivileges.Verified, DateTimeOffset.UnixEpoch); // restricted
        _sessionRegistry.All.Returns([self, target]);
        var reader = new BanchoPacketReader(PacketWriter.WriteI32List([2]));

        await new UserStatsRequestHandler(_sessionRegistry).HandleAsync(self, reader);

        Assert.Empty(self.Dequeue());
    }

    [Fact]
    public async Task Handle_OwnId_ExcludedFromResults()
    {
        var self = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _sessionRegistry.All.Returns([self]);
        var reader = new BanchoPacketReader(PacketWriter.WriteI32List([1]));

        await new UserStatsRequestHandler(_sessionRegistry).HandleAsync(self, reader);

        Assert.Empty(self.Dequeue());
    }
}