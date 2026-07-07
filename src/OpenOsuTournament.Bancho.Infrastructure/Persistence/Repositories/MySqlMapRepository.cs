using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Application.Abstractions.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Beatmaps;

namespace OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMapRepository" />
public sealed class MySqlMapRepository(string connectionString) : IMapRepository
{
    private const string SelectColumns = """
                                         md5, id, set_id AS SetId, artist, title, version, creator,
                                         last_update AS LastUpdate, total_length AS TotalLength, max_combo AS MaxCombo,
                                         status, frozen, plays, passes, mode, bpm, cs, od, ar, hp, diff, filename
                                         """;

    // mode is tinyint(1) but not a boolean (0-11) — disable MySqlConnector's default coercion.
    private readonly string _connectionString = connectionString + ";TreatTinyAsBoolean=false";

    public async Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
        int? setId = null, CancellationToken cancellationToken = default)
    {
        if (id is null && md5 is null && filename is null && setId is null)
            throw new ArgumentException("Must provide at least one of id/md5/filename/setId.");

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (id is not null)
        {
            conditions.Add("id = @Id");
            parameters.Add("Id", id);
        }

        if (md5 is not null)
        {
            conditions.Add("md5 = @Md5");
            parameters.Add("Md5", md5);
        }

        if (filename is not null)
        {
            conditions.Add("filename = @Filename");
            parameters.Add("Filename", filename);
        }

        if (setId is not null)
        {
            conditions.Add("set_id = @SetId");
            parameters.Add("SetId", setId);
        }

        await using var connection = Connect();
        // QueryFirstOrDefault, not QuerySingle: id/md5/filename each match at most one row (unique
        // constraints), but setId can match several maps within the same set — any one will do.
        var row = await connection.QueryFirstOrDefaultAsync<MapRow>(
            $"SELECT {SelectColumns} FROM maps WHERE {string.Join(" AND ", conditions)}",
            parameters);
        return row?.ToBeatmap();
    }

    public async Task UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            """
            REPLACE INTO maps (
                md5, id, server, set_id, artist, title, version, creator, filename, last_update,
                total_length, max_combo, status, frozen, plays, passes, mode, bpm, cs, od, ar, hp, diff
            ) VALUES (
                @Md5, @Id, 'osu!', @SetId, @Artist, @Title, @Version, @Creator, @Filename, @LastUpdate,
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
        await connection.ExecuteAsync("DELETE FROM maps WHERE md5 = @Md5", new { Md5 = md5 });
    }

    public async Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
        string? query, GameMode? mode, RankedStatus? status, int offset, int amount,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (query is not null)
        {
            conditions.Add("(artist LIKE @Query OR title LIKE @Query OR creator LIKE @Query)");
            parameters.Add("Query", $"%{query}%");
        }

        if (mode is not null)
        {
            conditions.Add("mode = @Mode");
            parameters.Add("Mode", (int)mode);
        }

        if (status is not null)
        {
            conditions.Add("status = @Status");
            parameters.Add("Status", (int)status);
        }

        var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";
        parameters.Add("Offset", offset);
        parameters.Add("Amount", amount);

        await using var connection = Connect();
        var setIds = (await connection.QueryAsync<int>(
            $"SELECT DISTINCT set_id FROM maps {whereClause} ORDER BY set_id DESC LIMIT @Amount OFFSET @Offset",
            parameters)).ToList();

        if (setIds.Count == 0) return [];

        var rows = await connection.QueryAsync<MapRow>(
            $"SELECT {SelectColumns} FROM maps WHERE set_id IN @SetIds ORDER BY diff ASC",
            new { SetIds = setIds });

        var mapsBySet = rows.Select(r => r.ToBeatmap()).GroupBy(b => b.SetId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Beatmap>)g.ToList());

        return setIds.Where(mapsBySet.ContainsKey).Select(id => mapsBySet[id]).ToList();
    }

    public async Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            $"UPDATE maps SET plays = plays + 1{(passed ? ", passes = passes + 1" : "")} WHERE id = @MapId",
            new { MapId = mapId });
    }

    private MySqlConnection Connect()
    {
        return new MySqlConnection(_connectionString);
    }

    // Mutable DTO so Dapper maps by property name instead of strict positional-constructor-type
    // matching, same pattern as MySqlUserRepository (mode is tinyint(1) but not boolean).
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