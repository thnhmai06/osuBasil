using Basil.Application.Sessions;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;

namespace Basil.Application.Tests.Sessions;

/// <summary>
///     Ported from app/objects/player.py's Player.stats (dict[GameMode, ModeData], populated once at
///     login via stats_from_sql_full — never re-queried per packet) + the gm_stats property
///     (`self.stats[self.status.mode]`).
/// </summary>
public class PlayerSessionStatsTests
{
    [Fact]
    public void CurrentStats_IndexesByCurrentStatusMode()
    {
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        session.ModeStats[GameMode.Standard] = new CachedPlayerStats(100, 90, 95.5, 10, 5);
        session.ModeStats[GameMode.Taiko] = new CachedPlayerStats(200, 180, 90.0, 20, 12);

        Assert.Equal(5, session.CurrentStats!.Rank);

        session.Status.Mode = GameMode.Taiko;
        Assert.Equal(12, session.CurrentStats!.Rank);
    }

    [Fact]
    public void CurrentStats_NoEntryForMode_ReturnsNull()
    {
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);

        Assert.Null(session.CurrentStats);
    }
}