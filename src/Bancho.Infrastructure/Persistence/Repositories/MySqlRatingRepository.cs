using Bancho.Application.Abstractions.Beatmaps;
using Dapper;
using MySqlConnector;

namespace Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IRatingRepository" />
public sealed class MySqlRatingRepository(string connectionString) : IRatingRepository
{
    public async Task<double> FetchAverageRatingAsync(string mapMd5, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(connectionString);
        var average = await connection.QuerySingleOrDefaultAsync<double?>(
            "SELECT AVG(rating) FROM ratings WHERE map_md5 = @MapMd5",
            new { MapMd5 = mapMd5 });
        return average ?? 0.0;
    }
}