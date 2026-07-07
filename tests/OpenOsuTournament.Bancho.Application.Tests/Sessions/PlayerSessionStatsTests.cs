using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Users;

namespace OpenOsuTournament.Bancho.Application.Tests.Sessions;

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
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        session.ModeStats[GameMode.VanillaOsu] = new CachedPlayerStats(100, 90, 95.5, 10, 500, 200, 300, 5);
        session.ModeStats[GameMode.VanillaTaiko] = new CachedPlayerStats(200, 180, 90.0, 20, 800, 150, 400, 12);

        Assert.Equal(5, session.CurrentStats!.Rank);

        session.Status.Mode = GameMode.VanillaTaiko;
        Assert.Equal(12, session.CurrentStats!.Rank);
    }

    [Fact]
    public void CurrentStats_NoEntryForMode_ReturnsNull()
    {
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        Assert.Null(session.CurrentStats);
    }
}