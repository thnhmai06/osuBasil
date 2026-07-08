using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;
using Dapper;
using MySqlConnector;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMapRepository" />
public sealed class MySqlMapRepository(string connectionString) : IMapRepository
{
    public async Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
        int? setId = null, CancellationToken cancellationToken = default)
    {
        if (id is null && md5 is null && filename is null && setId is null)
            throw new ArgumentException("Must provide at least one of id/md5/filename/setId.");

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (id is not null)
        {
            conditions.Add("Id = @Id");
            parameters.Add("Id", id);
        }

        if (md5 is not null)
        {
            conditions.Add("Md5 = @Md5");
            parameters.Add("Md5", md5);
        }

        if (filename is not null)
        {
            conditions.Add("Filename = @Filename");
            parameters.Add("Filename", filename);
        }

        if (setId is not null)
        {
            conditions.Add("SetId = @SetId");
            parameters.Add("SetId", setId);
        }

        await using var connection = Connect();
        // QueryFirstOrDefault, not QuerySingle: id/md5/filename each match at most one row (unique
        // constraints), but setId can match several maps within the same set — any one will do.
        var row = await connection.QueryFirstOrDefaultAsync<MapRow>(
            $"SELECT * FROM Beatmaps WHERE {string.Join(" AND ", conditions)}",
            parameters);
        return row?.ToBeatmap();
    }

    public async Task UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            """
            REPLACE INTO Beatmaps (
                Md5, Id, SetId, Artist, Title, Version, Creator, Filename, LastUpdate,
                TotalLength, MaxCombo, Status, Frozen, Plays, Passes, Mode, Bpm, Cs, Od, Ar, Hp, Diff
            ) VALUES (
                @Md5, @Id, @SetId, @Artist, @Title, @Version, @Creator, @Filename, @LastUpdate,
                @TotalLength, @MaxCombo, @Status, @Frozen, @Plays, @Passes, @Mode, @Bpm, @Cs, @Od, @Ar, @Hp, @Diff
            )
            """,
            new
            {
                beatmap.Md5,
                beatmap.Id,
                beatmap.SetId,
                beatmap.Artist,
                beatmap.Title,
                beatmap.Version,
                beatmap.Creator,
                beatmap.Filename,
                beatmap.LastUpdate,
                beatmap.TotalLength,
                beatmap.MaxCombo,
                Status = (int)beatmap.Status,
                beatmap.Frozen,
                beatmap.Plays,
                beatmap.Passes,
                Mode = (int)beatmap.Mode,
                beatmap.Bpm,
                beatmap.Cs,
                beatmap.Od,
                beatmap.Ar,
                beatmap.Hp,
                beatmap.Diff
            });
    }

    public async Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync("DELETE FROM Beatmaps WHERE Md5 = @Md5", new { Md5 = md5 });
    }

    public async Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
        string? query, GameMode? mode, RankedStatus? status, int offset, int amount,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (query is not null)
        {
            conditions.Add("(Artist LIKE @Query OR Title LIKE @Query OR Creator LIKE @Query)");
            parameters.Add("Query", $"%{query}%");
        }

        if (mode is not null)
        {
            conditions.Add("Mode = @Mode");
            parameters.Add("Mode", (int)mode);
        }

        if (status is not null)
        {
            conditions.Add("Status = @Status");
            parameters.Add("Status", (int)status);
        }

        var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";
        parameters.Add("Offset", offset);
        parameters.Add("Amount", amount);

        await using var connection = Connect();
        var setIds = (await connection.QueryAsync<int>(
            $"SELECT DISTINCT SetId FROM Beatmaps {whereClause} ORDER BY SetId DESC LIMIT @Amount OFFSET @Offset",
            parameters)).ToList();

        if (setIds.Count == 0) return [];

        var rows = await connection.QueryAsync<MapRow>(
            "SELECT * FROM Beatmaps WHERE SetId IN @SetIds ORDER BY Diff ASC",
            new { SetIds = setIds });

        var mapsBySet = rows.Select(r => r.ToBeatmap()).GroupBy(b => b.SetId)
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
        await connection.ExecuteAsync("UPDATE Beatmaps SET Diff = @Diff WHERE Id = @Id", new { Id = id, Diff = diff });
    }

    public async Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<MapRow>("SELECT * FROM Beatmaps WHERE SetId = @SetId",
            new { SetId = setId });
        return rows.Select(r => r.ToBeatmap()).ToList();
    }

    private MySqlConnection Connect()
    {
        return new MySqlConnection(connectionString);
    }

    // Mutable DTO so Dapper maps by property name instead of strict positional-constructor-type matching.
    private sealed class MapRow
    {
        public string Md5 { get; set; } = "";
        public int Id { get; set; }
        public int SetId { get; set; }
        public string Artist { get; set; } = "";
        public string Title { get; set; } = "";
        public string Version { get; set; } = "";
        public string Creator { get; set; } = "";
        public DateTime LastUpdate { get; set; }
        public int TotalLength { get; set; }
        public int MaxCombo { get; set; }
        public int Status { get; set; }
        public bool Frozen { get; set; }
        public int Plays { get; set; }
        public int Passes { get; set; }
        public int Mode { get; set; }
        public double Bpm { get; set; }
        public double Cs { get; set; }
        public double Od { get; set; }
        public double Ar { get; set; }
        public double Hp { get; set; }
        public double Diff { get; set; }
        public string Filename { get; set; } = "";

        public Beatmap ToBeatmap()
        {
            return new Beatmap(
                Md5, Id, SetId, Artist, Title, Version, Creator, LastUpdate, TotalLength, MaxCombo,
                (RankedStatus)Status, Frozen, Plays, Passes, (GameMode)Mode, Bpm, Cs, Od, Ar, Hp, Diff, Filename);
        }
    }
}