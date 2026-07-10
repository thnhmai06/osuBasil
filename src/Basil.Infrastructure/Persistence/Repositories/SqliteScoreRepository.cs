using Basil.Application.Abstractions.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IScoreRepository" />
public sealed class SqliteScoreRepository(string connectionString) : IScoreRepository
{
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
            "s.MapMd5 = @MapMd5",
            "s.Status = @Best",
            "((u.Priv & @Unrestricted) != 0 OR u.Id = @UserId)",
            "s.Mode = @Mode"
        };
        var parameters = new DynamicParameters();
        parameters.Add("MapMd5", mapMd5);
        parameters.Add("Best", (int)SubmissionStatus.Best);
        parameters.Add("Unrestricted", (int)Privileges.Unrestricted);
        parameters.Add("UserId", userId);
        parameters.Add("Mode", (int)mode);

        if (mods is not null)
        {
            conditions.Add("s.Mods = @Mods");
            parameters.Add("Mods", mods);
        }

        if (friendIds is not null)
        {
            conditions.Add("s.UserId IN @FriendIds");
            parameters.Add("FriendIds", friendIds);
        }

        if (country is not null)
        {
            conditions.Add("u.Country = @Country");
            parameters.Add("Country", country);
        }

        parameters.Add("Limit", limit);

        await using var connection = Connect();
        var rows = await connection.QueryAsync<BeatmapLeaderboardScoreRowDto>(
            $"""
             SELECT s.Id, s.Score AS Score, s.MaxCombo AS MaxCombo, s.N50 AS N50, s.N100 AS N100,
                    s.N300 AS N300, s.NMiss AS NMiss, s.NKatu AS NKatu, s.NGeki AS NGeki,
                    s.Perfect AS Perfect, s.Mods AS Mods, unixepoch(s.PlayTime) AS Time,
                    u.Id AS UserId, u.Name AS Name
             FROM Scores s
             JOIN Users u ON u.Id = s.UserId
             WHERE {string.Join(" AND ", conditions)}
             ORDER BY s.Score DESC
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
            SELECT Id, Score AS Score, MaxCombo AS MaxCombo, N50 AS N50, N100 AS N100,
                   N300 AS N300, NMiss AS NMiss, NKatu AS NKatu, NGeki AS NGeki,
                   Perfect AS Perfect, Mods AS Mods, unixepoch(PlayTime) AS Time, Grade AS Grade, Acc AS Acc
            FROM Scores
            WHERE MapMd5 = @MapMd5 AND Mode = @Mode AND UserId = @UserId AND Status = @Best
            ORDER BY Score DESC
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
            FROM Scores s
            JOIN Users u ON u.Id = s.UserId
            WHERE s.MapMd5 = @MapMd5 AND s.Mode = @Mode AND s.Status = @Best
              AND (u.Priv & @Unrestricted) != 0 AND s.Score > @Score
            """,
            new
            {
                MapMd5 = mapMd5, Mode = (int)mode, Best = (int)SubmissionStatus.Best,
                Unrestricted = (int)Privileges.Unrestricted, Score = score
            });
        return higherScores + 1;
    }

    public async Task<ScoreOwnerRow?> FetchOwnerAsync(long scoreId, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<ScoreOwnerRowDto>(
            "SELECT UserId AS UserId, Mode AS Mode FROM Scores WHERE Id = @ScoreId",
            new { ScoreId = scoreId });
        return row?.ToRow();
    }

    public async Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.QuerySingleAsync<long>(
            """
            INSERT INTO Scores
                (MapMd5, Score, Acc, MaxCombo, Mods, N300, N100, N50, NMiss, NGeki, NKatu,
                 Grade, Status, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect, OnlineChecksum, RoundId, Team, SubmittedAt)
            VALUES
                (@MapMd5, @Score, @Acc, @MaxCombo, @Mods, @N300, @N100, @N50, @NMiss, @NGeki, @NKatu,
                 @Grade, @Status, @Mode, @PlayTime, @TimeElapsed, @ClientFlags, @UserId, @Perfect, @OnlineChecksum, @RoundId, @Team, @SubmittedAt);
            SELECT last_insert_rowid();
            """,
            row);
    }

    public async Task<bool> ExistsByOnlineChecksumAsync(string onlineChecksum,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM Scores WHERE OnlineChecksum = @OnlineChecksum)",
            new { OnlineChecksum = onlineChecksum });
    }

    public async Task MarkPreviousBestScoresSubmittedAsync(string mapMd5, int userId, GameMode mode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            """
            UPDATE Scores SET Status = @Submitted
            WHERE Status = @Best AND MapMd5 = @MapMd5 AND UserId = @UserId AND Mode = @Mode
            """,
            new
            {
                Submitted = (int)SubmissionStatus.Submitted, Best = (int)SubmissionStatus.Best,
                MapMd5 = mapMd5, UserId = userId, Mode = (int)mode
            });
    }

    public async Task<FirstPlaceScoreRow?> FetchFirstPlaceScoreAsync(string mapMd5, GameMode mode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<FirstPlaceScoreRowDto>(
            """
            SELECT u.Id AS Id, u.Name AS Name
            FROM Users u
            JOIN Scores s ON u.Id = s.UserId
            WHERE s.MapMd5 = @MapMd5 AND s.Mode = @Mode AND s.Status = @Best
              AND (u.Priv & @Unrestricted) != 0
            ORDER BY s.Score DESC
            LIMIT 1
            """,
            new
            {
                MapMd5 = mapMd5, Mode = (int)mode, Best = (int)SubmissionStatus.Best,
                Unrestricted = (int)Privileges.Unrestricted
            });
        return row?.ToRow();
    }

    public async Task<IReadOnlyList<RoundScoreRow>> FetchByRoundIdAsync(int roundId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<RoundScoreRowDto>(
            """
            SELECT s.Id, s.UserId, u.Name AS UserName, s.Team, s.Mods, s.Score, s.Acc, s.MaxCombo,
                   s.N300, s.N100, s.N50, s.NMiss, s.NGeki, s.NKatu, s.Grade, s.Perfect, s.SubmittedAt
            FROM Scores s
            JOIN Users u ON u.Id = s.UserId
            WHERE s.RoundId = @RoundId
            ORDER BY s.Score DESC
            """,
            new { RoundId = roundId });
        return rows.Select(r => r.ToRow()).ToList();
    }

    private SqliteConnection Connect()
    {
        return new SqliteConnection(connectionString);
    }

    private sealed class RoundScoreRowDto
    {
        public long Id { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = "";
        public int? Team { get; set; }
        public int Mods { get; set; }
        public long Score { get; set; }
        public double Acc { get; set; }
        public int MaxCombo { get; set; }
        public int N300 { get; set; }
        public int N100 { get; set; }
        public int N50 { get; set; }
        public int NMiss { get; set; }
        public int NGeki { get; set; }
        public int NKatu { get; set; }
        public string Grade { get; set; } = "N";
        public bool Perfect { get; set; }
        public DateTime SubmittedAt { get; set; }

        public RoundScoreRow ToRow()
        {
            return new RoundScoreRow(
                Id, UserId, UserName, Team, Mods, Score, Acc, MaxCombo, N300, N100, N50, NMiss, NGeki, NKatu,
                Grade, Perfect, SubmittedAt);
        }
    }

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

        public BeatmapLeaderboardScoreRow ToRow()
        {
            return new BeatmapLeaderboardScoreRow(
                Id, Score, MaxCombo, N50, N100, N300, NMiss, NKatu, NGeki, Perfect != 0, Mods, Time, UserId, Name);
        }
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
        public string Grade { get; set; } = "N";
        public double Acc { get; set; }

        public PersonalBestLeaderboardScoreRow ToRow()
        {
            return new PersonalBestLeaderboardScoreRow(
                Id, Score, MaxCombo, N50, N100, N300, NMiss, NKatu, NGeki, Perfect != 0, Mods, Time, Grade, Acc);
        }
    }

    private sealed class ScoreOwnerRowDto
    {
        public int UserId { get; set; }
        public int Mode { get; set; }

        public ScoreOwnerRow ToRow()
        {
            return new ScoreOwnerRow(UserId, (GameMode)Mode);
        }
    }

    private sealed class FirstPlaceScoreRowDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public FirstPlaceScoreRow ToRow()
        {
            return new FirstPlaceScoreRow(Id, Name);
        }
    }
}
