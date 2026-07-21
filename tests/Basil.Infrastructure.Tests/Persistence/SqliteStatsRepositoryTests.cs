using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Ported from app/repositories/stats.py, scoped to what login (Player.stats_from_sql_full)
///     needs: fetch all per-mode stat rows for a user. migrations/base.sql seeds 8 mode rows for the
///     BasilBot user (id=1).
/// </summary>
public class SqliteStatsRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private readonly SqliteStatsRepository _repository = new(fixture.ConnectionString);

    [Fact]
    public async Task FetchAllForUser_SeededBasilBot_ReturnsEightModes()
    {
        var stats = await _repository.FetchAllForUserAsync(0);

        Assert.Equal(8, stats.Count);
        Assert.Contains(stats, s => (int)s.Mode == 0);
        Assert.Contains(stats, s => (int)s.Mode == 8); // ap!std
        Assert.All(stats, s => Assert.Equal(0, s.Id));
    }

    [Fact]
    public async Task FetchAllForUser_UnknownUser_ReturnsEmpty()
    {
        var stats = await _repository.FetchAllForUserAsync(999_999);

        Assert.Empty(stats);
    }

}