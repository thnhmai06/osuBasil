using Basil.Application.Abstractions.Scores;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ILeaderboardStore" />
/// <remarks>
///     Ranks are computed live from the UserStats table on every read: a player's rank is the
///     count of users with a higher ranked score in the mode, plus one. There is no separately
///     maintained leaderboard index, so the add/remove methods are no-ops.
/// </remarks>
public sealed class SqliteLeaderboardStore(string connectionString) : ILeaderboardStore
{
	/// <inheritdoc />
	/// <remarks>
	///     When the player has no UserStats row for the mode, the read returns
	///     <see langword="null" /> before any count query runs.
	/// </remarks>
	public async Task<int?> FetchGlobalRankAsync(int playerId, GameMode mode,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var ownScore = await connection.QuerySingleOrDefaultAsync<long?>(
			"SELECT RankedScore FROM UserStats WHERE Id = @UserId AND Mode = @Mode",
			new { PlayerId = playerId, Mode = (int)mode });
		if (ownScore is null) return null;

		var higherCount = await connection.QuerySingleAsync<int>(
			"SELECT COUNT(*) FROM UserStats WHERE Mode = @Mode AND RankedScore > @OwnScore",
			new { Mode = (int)mode, OwnScore = ownScore });
		return higherCount + 1;
	}

	/// <inheritdoc />
	/// <remarks>
	///     The count query joins UserStats to Users to restrict the ranking to the given country
	///     acronym. When the player has no UserStats row for the mode, the read returns
	///     <see langword="null" /> before any count query runs.
	/// </remarks>
	public async Task<int?> FetchCountryRankAsync(int playerId, GameMode mode, string country,
		CancellationToken cancellationToken = default)
	{
		await using var connection = Connect();
		var ownScore = await connection.QuerySingleOrDefaultAsync<long?>(
			"SELECT RankedScore FROM UserStats WHERE Id = @UserId AND Mode = @Mode",
			new { PlayerId = playerId, Mode = (int)mode });
		if (ownScore is null) return null;

		var higherCount = await connection.QuerySingleAsync<int>(
			"""
			SELECT COUNT(*)
			FROM UserStats us
			JOIN Users u ON u.Id = us.Id
			WHERE us.Mode = @Mode AND u.Country = @Country AND us.RankedScore > @OwnScore
			""",
			new { Mode = (int)mode, Country = country, OwnScore = ownScore });
		return higherCount + 1;
	}

	/// <inheritdoc />
	/// <remarks>
	///     A no-op: rank is computed live from UserStats by <see cref="FetchGlobalRankAsync" />, so
	///     there is no separate leaderboard index to keep in sync.
	/// </remarks>
	public Task AddToGlobalLeaderboardAsync(int playerId, GameMode mode, double score,
		CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	/// <remarks>
	///     A no-op: rank is computed live from UserStats by <see cref="FetchGlobalRankAsync" />, so
	///     there is no separate leaderboard index to keep in sync.
	/// </remarks>
	public Task RemoveFromGlobalLeaderboardAsync(int playerId, GameMode mode,
		CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	/// <remarks>
	///     A no-op: rank is computed live from UserStats by <see cref="FetchCountryRankAsync" />, so
	///     there is no separate leaderboard index to keep in sync.
	/// </remarks>
	public Task AddToCountryLeaderboardAsync(int playerId, GameMode mode, string country, double score,
		CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	/// <remarks>
	///     A no-op: rank is computed live from UserStats by <see cref="FetchCountryRankAsync" />, so
	///     there is no separate leaderboard index to keep in sync.
	/// </remarks>
	public Task RemoveFromCountryLeaderboardAsync(int playerId, GameMode mode, string country,
		CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	/// <summary>Creates a new SQLite connection using the store's connection string.</summary>
	private SqliteConnection Connect()
	{
		return new SqliteConnection(connectionString);
	}
}