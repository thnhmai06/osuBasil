using Bancho.Application.Abstractions;
using Bancho.Domain;
using Dapper;
using MySqlConnector;

namespace Bancho.Infrastructure.Persistence;

/// <inheritdoc cref="IScoreRepository" />
public sealed class MySqlScoreRepository(string connectionString) : IScoreRepository
{
    // mode/status are declared without the (1) width in scores' schema, but disable the
    // TreatTinyAsBoolean heuristic anyway for consistency with every other repo touching
    // tinyint-typed mode/status columns (see MySqlUserRepository's note on this class of bug).
    private readonly string _connectionString = connectionString + ";TreatTinyAsBoolean=false";

    private sealed class BeatmapLeaderboardScoreRowDto
    {
        public long Id { get; set; }
        public long Score { get; set; }
        public int MaxCombo { get; set; }
        public int N50 { get; set; }
        public int N100 { get; set; }
        public int N300 { get; set; }
        public int NMiss { get; set; }
        public int NKatu { get; set; }
        public int NGeki { get; set; }
        public int Perfect { get; set; }
        public int Mods { get; set; }
        public long Time { get; set; }
        public int UserId { get; set; }
        public string Name { get; set; } = "";

        public BeatmapLeaderboardScoreRow ToRow() => new(
            Id, Score, MaxCombo, N50, N100, N300, NMiss, NKatu, NGeki, Perfect != 0, Mods, Time, UserId, Name);
    }

    private sealed class PersonalBestLeaderboardScoreRowDto
    {
        public long Id { get; set; }
        public long Score { get; set; }
        public int MaxCombo { get; set; }
        public int N50 { get; set; }
        public int N100 { get; set; }
        public int N300 { get; set; }
        public int NMiss { get; set; }
        public int NKatu { get; set; }
        public int NGeki { get; set; }
        public int Perfect { get; set; }
        public int Mods { get; set; }
        public long Time { get; set; }

        public PersonalBestLeaderboardScoreRow ToRow() => new(
            Id, Score, MaxCombo, N50, N100, N300, NMiss, NKatu, NGeki, Perfect != 0, Mods, Time);
    }

    private MySqlConnection Connect() => new(_connectionString);

    public async Task<IReadOnlyList<BeatmapLeaderboardScoreRow>> FetchBeatmapLeaderboardScoresAsync(
        string mapMd5,
        GameMode mode,
        int userId,
        int? mods = null,
        IReadOnlySet<int>? friendIds = null,
        string? country = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>
        {
            "s.map_md5 = @MapMd5",
            "s.status = @Best",
            "((u.priv & @Unrestricted) != 0 OR u.id = @UserId)",
            "s.mode = @Mode",
        };
        var parameters = new DynamicParameters();
        parameters.Add("MapMd5", mapMd5);
        parameters.Add("Best", (int)SubmissionStatus.Best);
        parameters.Add("Unrestricted", (int)Privileges.Unrestricted);
        parameters.Add("UserId", userId);
        parameters.Add("Mode", (int)mode);

        if (mods is not null)
        {
            conditions.Add("s.mods = @Mods");
            parameters.Add("Mods", mods);
        }

        if (friendIds is not null)
        {
            conditions.Add("s.userid IN @FriendIds");
            parameters.Add("FriendIds", friendIds);
        }

        if (country is not null)
        {
            conditions.Add("u.country = @Country");
            parameters.Add("Country", country);
        }

        parameters.Add("Limit", limit);

        await using var connection = Connect();
        var rows = await connection.QueryAsync<BeatmapLeaderboardScoreRowDto>(
            $"""
            SELECT s.id, s.score AS Score, s.max_combo AS MaxCombo, s.n50 AS N50, s.n100 AS N100,
                   s.n300 AS N300, s.nmiss AS NMiss, s.nkatu AS NKatu, s.ngeki AS NGeki,
                   s.perfect AS Perfect, s.mods AS Mods, UNIX_TIMESTAMP(s.play_time) AS Time,
                   u.id AS UserId, u.name AS Name
            FROM scores s
            JOIN users u ON u.id = s.userid
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY s.score DESC
            LIMIT @Limit
            """,
            parameters);

        return rows.Select(r => r.ToRow()).ToList();
    }

    public async Task<PersonalBestLeaderboardScoreRow?> FetchPersonalBestLeaderboardScoreAsync(
        string mapMd5, GameMode mode, int userId, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<PersonalBestLeaderboardScoreRowDto>(
            """
            SELECT id, score AS Score, max_combo AS MaxCombo, n50 AS N50, n100 AS N100,
                   n300 AS N300, nmiss AS NMiss, nkatu AS NKatu, ngeki AS NGeki,
                   perfect AS Perfect, mods AS Mods, UNIX_TIMESTAMP(play_time) AS Time
            FROM scores
            WHERE map_md5 = @MapMd5 AND mode = @Mode AND userid = @UserId AND status = @Best
            ORDER BY score DESC
            LIMIT 1
            """,
            new { MapMd5 = mapMd5, Mode = (int)mode, UserId = userId, Best = (int)SubmissionStatus.Best });
        return row?.ToRow();
    }

    public async Task<int> FetchPersonalBestLeaderboardRankAsync(
        string mapMd5, GameMode mode, long score, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var higherScores = await connection.QuerySingleAsync<int>(
            """
            SELECT COUNT(*)
            FROM scores s
            JOIN users u ON u.id = s.userid
            WHERE s.map_md5 = @MapMd5 AND s.mode = @Mode AND s.status = @Best
              AND (u.priv & @Unrestricted) != 0 AND s.score > @Score
            """,
            new
            {
                MapMd5 = mapMd5, Mode = (int)mode, Best = (int)SubmissionStatus.Best,
                Unrestricted = (int)Privileges.Unrestricted, Score = score,
            });
        return higherScores + 1;
    }
}
