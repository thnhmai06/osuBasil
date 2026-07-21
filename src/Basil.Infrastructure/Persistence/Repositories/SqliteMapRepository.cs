using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMapRepository" />
public sealed class SqliteMapRepository(string connectionString) : IMapRepository
{
    private const string SharedColumns = """
        b.Md5, b.Id, b.Version, b.Filename, b.TotalLength, b.MaxCombo, b.Frozen, b.Plays, b.Passes,
        b.Mode, b.Bpm, b.Cs, b.Ar, b.Od, b.Hp, b.Sr,
        m.Id, m.Artist, m.Title, m.Creator, m.LastUpdate, m.CreatedAt
        """;

    public async Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
        int? setId = null, bool includeFrozen = false, CancellationToken cancellationToken = default)
    {
        if (id is null && md5 is null && filename is null && setId is null)
            throw new ArgumentException("Must provide at least one of id/md5/filename/setId.");

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (id is not null)
        {
            conditions.Add("b.Id = @Id");
            parameters.Add("Id", id);
        }

        if (md5 is not null)
        {
            conditions.Add("b.Md5 = @Md5");
            parameters.Add("Md5", md5);
        }

        if (filename is not null)
        {
            conditions.Add("b.Filename = @Filename");
            parameters.Add("Filename", filename);
        }

        if (setId is not null)
        {
            conditions.Add("b.MapsetId = @MapsetId");
            parameters.Add("MapsetId", setId);
        }

        if (!includeFrozen) conditions.Add("b.Frozen = 0");

        await using var connection = Connect();
        // Dapper has no multi-map QueryFirstOrDefaultAsync overload — QueryAsync + FirstOrDefault.
        // id/md5/filename each match at most one row (unique constraints), but setId can match
        // several maps within the same set — any one will do.
        var beatmaps = await connection.QueryAsync<BeatmapRow, MapsetRow, Beatmap>(
            $"""
             SELECT {SharedColumns} FROM Beatmaps b JOIN Mapsets m ON b.MapsetId = m.Id
             WHERE {string.Join(" AND ", conditions)}
             """,
            (b, m) => b.ToBeatmap(m.ToMapset()),
            parameters,
            splitOn: "Id");
        return beatmaps.FirstOrDefault();
    }

    public async Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
    {
        var existing = await FetchOneAsync(md5: beatmap.Md5, includeFrozen: true, cancellationToken: cancellationToken);
        int resolvedId;
        if (existing is not null) resolvedId = existing.Id;
        else if (beatmap.Id > 0) resolvedId = beatmap.Id;
        else resolvedId = Math.Max(Beatmap.LocalIdFloor, await FetchMaxIdAsync(cancellationToken) + 1);

        var resolved = beatmap with { Id = resolvedId };

        await using var connection = Connect();
        await connection.ExecuteAsync(
            """
            REPLACE INTO Beatmaps (
                Md5, Id, MapsetId, Version, Filename, TotalLength, MaxCombo, Frozen, Plays, Passes,
                Mode, Bpm, Cs, Od, Ar, Hp, Sr
            ) VALUES (
                @Md5, @Id, @MapsetId, @Version, @Filename, @TotalLength, @MaxCombo, @Frozen, @Plays, @Passes,
                @Mode, @Bpm, @Cs, @Od, @Ar, @Hp, @Sr
            )
            """,
            new
            {
                resolved.Md5,
                resolved.Id,
                MapsetId = resolved.Mapset.Id,
                resolved.Version,
                resolved.Filename,
                TotalLength = (int)resolved.TotalLength.TotalSeconds,
                resolved.MaxCombo,
                Frozen = resolved.IsFrozen,
                resolved.Plays,
                resolved.Passes,
                Mode = (int)resolved.Difficulty.Mode,
                resolved.Difficulty.Bpm,
                resolved.Difficulty.Cs,
                resolved.Difficulty.Od,
                resolved.Difficulty.Ar,
                resolved.Difficulty.Hp,
                resolved.Difficulty.Sr
            });

        return resolved;
    }

    public async Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync("DELETE FROM Beatmaps WHERE Md5 = @Md5", new { Md5 = md5 });
    }

    public async Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
        string? query, GameMode? mode, int offset, int amount,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string> { "b.Frozen = 0" };
        var parameters = new DynamicParameters();

        if (query is not null)
        {
            conditions.Add("(m.Artist LIKE @Query OR m.Title LIKE @Query OR m.Creator LIKE @Query)");
            parameters.Add("Query", $"%{query}%");
        }

        if (mode is not null)
        {
            conditions.Add("b.Mode = @Mode");
            parameters.Add("Mode", (int)mode);
        }

        var whereClause = $"WHERE {string.Join(" AND ", conditions)}";
        parameters.Add("Offset", offset);
        parameters.Add("Amount", amount);

        await using var connection = Connect();
        var setIds = (await connection.QueryAsync<int>(
            $"""
             SELECT DISTINCT b.MapsetId FROM Beatmaps b JOIN Mapsets m ON b.MapsetId = m.Id
             {whereClause}
             ORDER BY b.MapsetId DESC LIMIT @Amount OFFSET @Offset
             """,
            parameters)).ToList();

        if (setIds.Count == 0) return [];

        var rows = await connection.QueryAsync<BeatmapRow, MapsetRow, Beatmap>(
            $"""
             SELECT {SharedColumns} FROM Beatmaps b JOIN Mapsets m ON b.MapsetId = m.Id
             WHERE b.MapsetId IN @SetIds AND b.Frozen = 0
             ORDER BY b.Sr ASC
             """,
            (b, m) => b.ToBeatmap(m.ToMapset()),
            new { SetIds = setIds },
            splitOn: "Id");

        var mapsBySet = rows.GroupBy(b => b.Mapset.Id)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Beatmap>)g.ToList());

        return setIds.Where(mapsBySet.ContainsKey).Select(id => mapsBySet[id]).ToList();
    }

    public async Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            $"UPDATE Beatmaps SET Plays = Plays + 1{(passed ? ", Passes = Passes + 1" : "")} WHERE Id = @MapId",
            new { MapId = mapId });
    }

    public async Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(Id), 0) FROM Beatmaps");
    }

    public async Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync("UPDATE Beatmaps SET Sr = @Sr WHERE Id = @Id", new { Id = id, Sr = diff });
    }

    public async Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includeFrozen = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var whereClause = includeFrozen ? "WHERE b.MapsetId = @MapsetId" : "WHERE b.MapsetId = @MapsetId AND b.Frozen = 0";
        var rows = await connection.QueryAsync<BeatmapRow, MapsetRow, Beatmap>(
            $"""
             SELECT {SharedColumns} FROM Beatmaps b JOIN Mapsets m ON b.MapsetId = m.Id
             {whereClause}
             """,
            (b, m) => b.ToBeatmap(m.ToMapset()),
            new { MapsetId = setId },
            splitOn: "Id");
        return rows.ToList();
    }

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }

    // Mutable DTOs so Dapper maps by property name instead of strict positional-constructor-type
    // matching. Split into Beatmaps-only and Mapsets-only halves for the JOIN's multi-mapping.
    private sealed class BeatmapRow
    {
        public string Md5 { get; set; } = "";
        public int Id { get; set; }
        public string Version { get; set; } = "";
        public string Filename { get; set; } = "";
        public int TotalLength { get; set; }
        public int MaxCombo { get; set; }
        public bool Frozen { get; set; }
        public int Plays { get; set; }
        public int Passes { get; set; }
        public int Mode { get; set; }
        public double Bpm { get; set; }
        public double Cs { get; set; }
        public double Ar { get; set; }
        public double Od { get; set; }
        public double Hp { get; set; }
        public double Sr { get; set; }

        public Beatmap ToBeatmap(Mapset mapset)
        {
            return new Beatmap(
                Md5, Id, mapset, Version, Filename,
                TimeSpan.FromSeconds(TotalLength), MaxCombo, Frozen, Plays, Passes,
                new Difficulty((GameMode)Mode, Bpm, Cs, Ar, Od, Hp, Sr));
        }
    }

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
