using Basil.Application.Abstractions.Users;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IUserStatRepository" />
/// <remarks>
///     Rows map through the private mutable <c>StatsRow</c> DTO; the stored mode column is cast to
///     <see cref="GameMode" /> during mapping. Each method opens its own connection.
/// </remarks>
public sealed class SqliteUserStatRepository(string connectionString) : IUserStatRepository
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<Stats>> FetchAllForUserAsync(int userId,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var rows = await connection.QueryAsync<StatsRow>(
			"SELECT * FROM UserStats WHERE Id = @UserId",
			new { UserId = userId });
		return [.. rows.Select(r => r.ToStats())];
	}

	/// <inheritdoc />
	/// <remarks>
	///     An upsert keyed on the user and mode: a user's first play in a mode inserts the stats
	///     row, and later plays add the deltas to the totals and bump the play count by one.
	/// </remarks>
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

	/// <summary>Creates a new SQLite connection using the repository's connection string.</summary>
	private SqliteConnection Connect()
	{
		return new SqliteConnection(connectionString);
	}

	/// <summary>
	///     A mutable row DTO matching the UserStats table columns.
	/// </summary>
	private sealed class StatsRow
	{
		public int Id { get; set; }
		public int Mode { get; set; }
		public long TotalScore { get; set; }
		public long RankedScore { get; set; }
		public int Plays { get; set; }

		/// <summary>Builds a <see cref="Stats" /> from this row, casting the stored mode column.</summary>
		/// <returns>The domain stats record.</returns>
		public Stats ToStats()
		{
			return new Stats(Id, (GameMode)Mode, TotalScore, RankedScore, Plays);
		}
	}
}