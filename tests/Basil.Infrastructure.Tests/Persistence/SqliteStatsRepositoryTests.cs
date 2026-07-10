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
        Assert.Contains(stats, s => s.Mode == 0);
        Assert.Contains(stats, s => s.Mode == 8); // ap!std
        Assert.All(stats, s => Assert.Equal(0, s.Id));
    }

    [Fact]
    public async Task FetchAllForUser_UnknownUser_ReturnsEmpty()
    {
        var stats = await _repository.FetchAllForUserAsync(999_999);

        Assert.Empty(stats);
    }

    [Fact]
    public async Task FetchOne_ReturnsSingleModeRow()
    {
        var stat = await _repository.FetchOneAsync(0, 0);

        Assert.NotNull(stat);
        Assert.Equal(0, stat.Mode);
        Assert.Equal(0, stat.Tscore);
        Assert.Equal(0, stat.Plays);
    }

    [Fact]
    public async Task IncrementReplayViews_AddsOneEachCall()
    {
        // mode 2 (catch), same isolation rationale as UpdateAfterScore_PersistsNewTotals above.
        await _repository.IncrementReplayViewsAsync(0, 2);
        await _repository.IncrementReplayViewsAsync(0, 2);

        var stat = await _repository.FetchOneAsync(0, 2);

        Assert.NotNull(stat);
        Assert.Equal(2, stat.ReplayViews);
    }
}