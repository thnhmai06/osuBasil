using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Verifies `SqliteUserStatRepository` fetches all per-mode stat rows for a user.
///     migrations/base.sql seeds 4 mode rows (Standard/Taiko/Catch/Mania) for the BasilBot user (id=0).
/// </summary>
public class SqliteUserStatRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
	private readonly SqliteUserStatRepository _repository = new(fixture.ConnectionString);

	[Fact]
	public async Task FetchAllForUser_SeededBasilBot_ReturnsAllSupportedModes()
	{
		var stats = await _repository.FetchAllForUserAsync(0);

		Assert.Equal(4, stats.Count);
		Assert.Contains(stats, s => s.Mode == GameMode.Standard);
		Assert.Contains(stats, s => s.Mode == GameMode.Taiko);
		Assert.Contains(stats, s => s.Mode == GameMode.Catch);
		Assert.Contains(stats, s => s.Mode == GameMode.Mania);
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
	///     FetchAllForUser_SeededBasilBot_ReturnsAllSupportedModes's row-count assertion.
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