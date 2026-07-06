namespace Bancho.Infrastructure.Tests.Persistence;

/// <summary>
/// Ported from app/repositories/stats.py, scoped to what login (Player.stats_from_sql_full)
/// needs: fetch all per-mode stat rows for a user. migrations/base.sql seeds 8 mode rows for the
/// BanchoBot user (id=1).
/// </summary>
public class MySqlStatsRepositoryTests : IClassFixture<MySqlFixture>
{
    private readonly Bancho.Infrastructure.Persistence.MySqlStatsRepository _repository;

    public MySqlStatsRepositoryTests(MySqlFixture fixture)
    {
        _repository = new Bancho.Infrastructure.Persistence.MySqlStatsRepository(fixture.ConnectionString);
    }

    [Fact]
    public async Task FetchAllForUser_SeededBanchoBot_ReturnsEightModes()
    {
        var stats = await _repository.FetchAllForUserAsync(1);

        Assert.Equal(8, stats.Count);
        Assert.Contains(stats, s => s.Mode == 0);
        Assert.Contains(stats, s => s.Mode == 8); // ap!std
        Assert.All(stats, s => Assert.Equal(1, s.Id));
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
        var stat = await _repository.FetchOneAsync(1, mode: 0);

        Assert.NotNull(stat);
        Assert.Equal(0, stat!.Mode);
        Assert.Equal(0, stat.Tscore);
        Assert.Equal(0, stat.Plays);
    }

    [Fact]
    public async Task UpdateAfterScore_PersistsNewTotals()
    {
        // uses mode 1 (taiko), not mode 0, so it doesn't collide with the other tests in this
        // class asserting on mode 0's still-default seeded values (fixture/container is shared
        // per IClassFixture, and xUnit does not guarantee test method execution order).
        await _repository.UpdateAfterScoreAsync(
            userId: 1,
            mode: 1,
            tscore: 1_000_000,
            rscore: 900_000,
            plays: 5,
            playtime: 300,
            acc: 98.5,
            maxCombo: 500,
            totalHits: 800,
            xhCount: 1,
            xCount: 2,
            shCount: 3,
            sCount: 4,
            aCount: 5);

        var stat = await _repository.FetchOneAsync(1, mode: 1);

        Assert.NotNull(stat);
        Assert.Equal(1_000_000, stat!.Tscore);
        Assert.Equal(900_000, stat.Rscore);
        Assert.Equal(5, stat.Plays);
        Assert.Equal(300, stat.Playtime);
        Assert.Equal(98.5, stat.Acc, precision: 2);
        Assert.Equal(500, stat.MaxCombo);
        Assert.Equal(800, stat.TotalHits);
        Assert.Equal(1, stat.XhCount);
        Assert.Equal(2, stat.XCount);
        Assert.Equal(3, stat.ShCount);
        Assert.Equal(4, stat.SCount);
        Assert.Equal(5, stat.ACount);
    }

    [Fact]
    public async Task IncrementReplayViews_AddsOneEachCall()
    {
        // mode 2 (catch), same isolation rationale as UpdateAfterScore_PersistsNewTotals above.
        await _repository.IncrementReplayViewsAsync(1, mode: 2);
        await _repository.IncrementReplayViewsAsync(1, mode: 2);

        var stat = await _repository.FetchOneAsync(1, mode: 2);

        Assert.NotNull(stat);
        Assert.Equal(2, stat!.ReplayViews);
    }
}
