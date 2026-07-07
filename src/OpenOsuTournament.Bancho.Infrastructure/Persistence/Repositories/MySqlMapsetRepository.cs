using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;

namespace OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMapsetRepository" />
public sealed class MySqlMapsetRepository(string connectionString) : IMapsetRepository
{
    public async Task EnsureExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync("INSERT IGNORE INTO Mapsets (Id) VALUES (@Id)", new { Id = id });
    }

    public async Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(Id), 0) FROM Mapsets");
    }

    private MySqlConnection Connect()
    {
        return new MySqlConnection(connectionString);
    }
}
