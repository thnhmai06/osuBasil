using Basil.Application.Abstractions.Users;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IStatsRepository" />
public sealed class SqliteStatsRepository(string connectionString) : IStatsRepository
{
	public async Task<IReadOnlyList<Stats>> FetchAllForUserAsync(int userId,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<StatsRow>(
			"SELECT * FROM UserStats WHERE Id = @UserId",
			new { UserId = userId });
		return [.. rows.Select(r => r.ToStats())];
	}

	public async Task IncrementAsync(int userId, GameMode mode, long totalScoreDelta, long rankedScoreDelta,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		await connection.ExecuteAsync(
			"""
			INSERT INTO UserStats (Id, Mode, TotalScore, RankedScore, Plays)
			VALUES (@UserId, @Mode, @TotalScoreDelta, @RankedScoreDelta, 1)
			ON CONFLICT(Id, Mode) DO UPDATE SET
			    TotalScore = TotalScore + @TotalScoreDelta,
			    RankedScore = RankedScore + @RankedScoreDelta,
			    Plays = Plays + 1
			""",
			new
			{
				UserId = userId, Mode = (int)mode, TotalScoreDelta = totalScoreDelta,
				RankedScoreDelta = rankedScoreDelta
			});
	}

	private SqliteConnection Connect()
	{
		return new SqliteConnection(connectionString);
	}

	private sealed class StatsRow
	{
		public int Id { get; set; }
		public int Mode { get; set; }
		public long TotalScore { get; set; }
		public long RankedScore { get; set; }
		public int Plays { get; set; }

		public Stats ToStats()
		{
			return new Stats(Id, (GameMode)Mode, TotalScore, RankedScore, Plays);
		}
	}
}
