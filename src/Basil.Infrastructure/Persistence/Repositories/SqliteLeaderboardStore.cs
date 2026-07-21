using Basil.Application.Abstractions.Scores;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ILeaderboardStore" />
public sealed class SqliteLeaderboardStore(string connectionString) : ILeaderboardStore
{
    public async Task<int?> FetchGlobalRankAsync(int playerId, GameMode mode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var ownScore = await connection.QuerySingleOrDefaultAsync<long?>(
            "SELECT Rscore FROM UserStats WHERE Id = @UserId AND Mode = @Mode",
            new { PlayerId = playerId, Mode = (int)mode });
        if (ownScore is null) return null;

        var higherCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM UserStats WHERE Mode = @Mode AND Rscore > @OwnScore",
            new { Mode = (int)mode, OwnScore = ownScore });
        return higherCount + 1;
    }

    public async Task<int?> FetchCountryRankAsync(int playerId, GameMode mode, string country,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var ownScore = await connection.QuerySingleOrDefaultAsync<long?>(
            "SELECT Rscore FROM UserStats WHERE Id = @UserId AND Mode = @Mode",
            new { PlayerId = playerId, Mode = (int)mode });
        if (ownScore is null) return null;

        var higherCount = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM UserStats us
            JOIN Users u ON u.Id = us.Id
            WHERE us.Mode = @Mode AND u.Country = @Country AND us.Rscore > @OwnScore
            """,
            new { Mode = (int)mode, Country = country, OwnScore = ownScore });
        return higherCount + 1;
    }

    // Rank is always computed live from UserStats (see Fetch*RankAsync) instead of a separately
    // maintained sorted set, so there is no index to keep in sync — these are no-ops. (Also: no
    // caller in this codebase ever invoked these on the previous Redis-backed implementation
    // either, so this isn't a behavior change for anything live.)
    public Task AddToGlobalLeaderboardAsync(int playerId, GameMode mode, double score,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RemoveFromGlobalLeaderboardAsync(int playerId, GameMode mode,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task AddToCountryLeaderboardAsync(int playerId, GameMode mode, string country, double score,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RemoveFromCountryLeaderboardAsync(int playerId, GameMode mode, string country,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }
}
