using Basil.Application.Abstractions.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IRatingRepository" />
public sealed class SqliteRatingRepository(string connectionString) : IRatingRepository
{
    public async Task<double> FetchAverageRatingAsync(string mapMd5, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        var average = await connection.QuerySingleOrDefaultAsync<double?>(
            "SELECT AVG(Rating) FROM Ratings WHERE MapMd5 = @MapMd5",
            new { MapMd5 = mapMd5 });
        return average ?? 0.0;
    }
}
