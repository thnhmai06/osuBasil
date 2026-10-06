using Basil.Application.Storage.Contracts.Scores;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Dapper;

namespace Basil.Infrastructure.Storage.Scores;

/// <summary>Stores users' cumulative score statistics.</summary>
internal sealed class SqliteUserStatsRepository(Database database) : IUserStatsRepository
{
	/// <inheritdoc />
	public async ValueTask<UserStats> GetAsync(User user, GameMode mode, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<StatsRow>(
			"SELECT UserId, Mode, TotalScore, RankedScore, PlayCount FROM UserStats WHERE UserId = @UserId AND Mode = @Mode",
			new { UserId = user.Id, Mode = (long)mode });

		return row is null
			? new UserStats { UserId = user.Id, Mode = mode }
			: new UserStats
			{
				UserId = row.UserId,
				Mode = (GameMode)row.Mode,
				TotalScore = row.TotalScore,
				RankedScore = row.RankedScore,
				PlayCount = row.PlayCount
			};
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(UserStats stats, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			INSERT INTO UserStats (UserId, Mode, TotalScore, RankedScore, PlayCount)
			VALUES (@UserId, @Mode, @TotalScore, @RankedScore, @PlayCount)
			ON CONFLICT(UserId, Mode) DO UPDATE SET
				TotalScore = excluded.TotalScore,
				RankedScore = excluded.RankedScore,
				PlayCount = excluded.PlayCount
			""",
			new
			{
				stats.UserId,
				Mode = (long)stats.Mode,
				stats.TotalScore,
				stats.RankedScore,
				stats.PlayCount
			});
	}

	/// <summary>A stored row of the UserStats table.</summary>
	private sealed class StatsRow
	{
		public int UserId { get; set; }
		public long Mode { get; set; }
		public long TotalScore { get; set; }
		public long RankedScore { get; set; }
		public int PlayCount { get; set; }
	}
}
