using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;

namespace OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IRatingRepository" />
public sealed class MySqlRatingRepository(string connectionString) : IRatingRepository
{
    public async Task<double> FetchAverageRatingAsync(string mapMd5, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(connectionString);
        var average = await connection.QuerySingleOrDefaultAsync<double?>(
            "SELECT AVG(Rating) FROM Ratings WHERE MapMd5 = @MapMd5",
            new { MapMd5 = mapMd5 });
        return average ?? 0.0;
    }
}
