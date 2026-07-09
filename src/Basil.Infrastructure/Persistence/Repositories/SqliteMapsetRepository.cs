using Basil.Application.Abstractions.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMapsetRepository" />
public sealed class SqliteMapsetRepository(string connectionString) : IMapsetRepository
{
    public async Task EnsureExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync("INSERT OR IGNORE INTO Mapsets (Id) VALUES (@Id)", new { Id = id });
    }

    public async Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(Id), 0) FROM Mapsets");
    }

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }
}
