using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMapsetRepository" />
public sealed class SqliteMapsetRepository(string connectionString) : IMapsetRepository
{
    public async Task<Mapset?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<MapsetRow>(
            "SELECT * FROM Mapsets WHERE Id = @Id", new { Id = id });
        return row?.ToMapset();
    }

    public async Task<Mapset> UpsertAsync(Mapset mapset, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        // INSERT ... ON CONFLICT DO UPDATE, not REPLACE INTO: REPLACE deletes-then-reinserts on a
        // PK conflict, and that delete cascades via Beatmaps_Mapsets_Id_fk (on delete cascade),
        // wiping every Beatmap under this Mapset on every re-upsert (e.g. every reconcile pass).
        await connection.ExecuteAsync(
            """
            INSERT INTO Mapsets (Id, Artist, Title, Creator, LastUpdate, CreatedAt)
            VALUES (@Id, @Artist, @Title, @Creator, @LastUpdate, @CreatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Artist = excluded.Artist, Title = excluded.Title, Creator = excluded.Creator,
                LastUpdate = excluded.LastUpdate, CreatedAt = excluded.CreatedAt
            """,
            new
            {
                mapset.Id,
                mapset.Artist,
                mapset.Title,
                mapset.Creator,
                mapset.LastUpdate,
                mapset.CreatedAt
            });
        return mapset;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        // Beatmaps rows cascade via Beatmaps_Mapsets_Id_fk (on delete cascade) — no manual cleanup needed.
        await connection.ExecuteAsync("DELETE FROM Mapsets WHERE Id = @Id", new { Id = id });
    }

    public async Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(Id), 0) FROM Mapsets");
    }

    public async Task<IReadOnlyList<int>> FetchAllIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var ids = await connection.QueryAsync<int>("SELECT Id FROM Mapsets");
        return ids.ToList();
    }

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }

    // Mutable DTO so Dapper maps by property name instead of strict positional-constructor-type matching.
    private sealed class MapsetRow
    {
        public int Id { get; set; }
        public string Artist { get; set; } = "";
        public string Title { get; set; } = "";
        public string Creator { get; set; } = "";
        public DateTime LastUpdate { get; set; }
        public DateTime CreatedAt { get; set; }

        public Mapset ToMapset()
        {
            return new Mapset(Id, Artist, Title, Creator, LastUpdate, CreatedAt);
        }
    }
}
