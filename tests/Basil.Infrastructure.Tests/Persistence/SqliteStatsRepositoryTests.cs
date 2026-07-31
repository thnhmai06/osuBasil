using Basil.Domain.Beatmaps;
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

	/// <summary>
	///     Uses BasilBot's own seeded Taiko row (untouched by the other tests in this class, which
	///     share the same SqliteFixture database) so this doesn't perturb
	///     FetchAllForUser_SeededBasilBot_ReturnsEightModes's row-count assertion.
	/// </summary>
	[Fact]
	public async Task IncrementAsync_CalledTwice_AccumulatesDeltasAndPlays()
	{
		await _repository.IncrementAsync(0, GameMode.Taiko, 1000, 500);
		await _repository.IncrementAsync(0, GameMode.Taiko, 2000, 0);

		var stats = await _repository.FetchAllForUserAsync(0);
		var row = stats.Single(s => s.Mode == GameMode.Taiko);
		Assert.Equal(3000, row.TotalScore);
		Assert.Equal(500, row.RankedScore);
		Assert.Equal(2, row.Plays);
	}
}