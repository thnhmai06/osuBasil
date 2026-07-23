using Basil.Application.Abstractions.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IScoreRepository" />
public sealed class SqliteScoreRepository(string connectionString) : IScoreRepository
{
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
                (MapMd5, Score, Accuracy, MaxCombo, Mods, N300, N100, N50, NMiss, NGeki, NKatu,
                 Grade, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect, OnlineChecksum, RoundId, Team, SubmittedAt)
            VALUES
                (@MapMd5, @Score, @Accuracy, @MaxCombo, @Mods, @N300, @N100, @N50, @NMiss, @NGeki, @NKatu,
                 @Grade, @Mode, @PlayTime, @TimeElapsed, @ClientFlags, @UserId, @Perfect, @OnlineChecksum, @RoundId, @Team, @SubmittedAt);
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

    public async Task<FirstPlaceScoreRow?> FetchFirstPlaceScoreAsync(string mapMd5, GameMode mode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<FirstPlaceScoreRowDto>(
            """
            SELECT u.Id AS Id, u.Name AS Name
            FROM Users u
            JOIN Scores s ON u.Id = s.UserId
            WHERE s.MapMd5 = @MapMd5 AND s.Mode = @Mode
              AND (u.Priv & @Unrestricted) != 0
            ORDER BY s.Score DESC
            LIMIT 1
            """,
            new
            {
                MapMd5 = mapMd5, Mode = (int)mode,
                Unrestricted = (int)UserPrivileges.Unrestricted
            });
        return row?.ToRow();
    }

    public async Task<ScoreRow?> FetchByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var row = await connection.QuerySingleOrDefaultAsync<ScoreRowDto>(
            """
            SELECT Id, RoundId, Team, MapMd5, Score, Accuracy, MaxCombo, Mods, N300, N100, N50, NMiss,
                   NGeki, NKatu, Grade, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect,
                   OnlineChecksum, SubmittedAt, IsInvalidated
            FROM Scores
            WHERE Id = @Id
            """,
            new { Id = id });
        return row?.ToRow();
    }

    public async Task<IReadOnlyList<RoundScoreRow>> FetchByRoundIdAsync(int roundId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        var rows = await connection.QueryAsync<RoundScoreRowDto>(
            """
            SELECT s.Id, s.UserId, u.Name AS UserName, s.Team, s.Mods, s.Score, s.Accuracy, s.MaxCombo,
                   s.N300, s.N100, s.N50, s.NMiss, s.NGeki, s.NKatu, s.Grade, s.Perfect, s.SubmittedAt
            FROM Scores s
            JOIN Users u ON u.Id = s.UserId
            WHERE s.RoundId = @RoundId
            ORDER BY s.Score DESC
            """,
            new { RoundId = roundId });
        return rows.Select(r => r.ToRow()).ToList();
    }

    public async Task InvalidateByMapMd5Async(string mapMd5, CancellationToken cancellationToken = default)
    {
        await using var connection = Connect();
        await connection.ExecuteAsync(
            "UPDATE Scores SET IsInvalidated = 1 WHERE MapMd5 = @MapMd5",
            new { MapMd5 = mapMd5 });
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
        public double Accuracy { get; set; }
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
                Id, UserId, UserName, (MatchTeam?)Team, (Mods)Mods, Score, Accuracy, MaxCombo, N300, N100, N50,
                NMiss, NGeki, NKatu, Grade, Perfect, SubmittedAt);
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

    private sealed class ScoreRowDto
    {
        public long Id { get; set; }
        public int? RoundId { get; set; }
        public int? Team { get; set; }
        public string MapMd5 { get; set; } = "";
        public long Score { get; set; }
        public double Accuracy { get; set; }
        public int MaxCombo { get; set; }
        public int Mods { get; set; }
        public int N300 { get; set; }
        public int N100 { get; set; }
        public int N50 { get; set; }
        public int NMiss { get; set; }
        public int NGeki { get; set; }
        public int NKatu { get; set; }
        public string Grade { get; set; } = "N";
        public int Mode { get; set; }
        public DateTime PlayTime { get; set; }
        public int TimeElapsed { get; set; }
        public int ClientFlags { get; set; }
        public int UserId { get; set; }
        public bool Perfect { get; set; }
        public string OnlineChecksum { get; set; } = "";
        public DateTime SubmittedAt { get; set; }
        public bool IsInvalidated { get; set; }

        public ScoreRow ToRow()
        {
            return new ScoreRow(
                Id, RoundId, (MatchTeam?)Team, MapMd5, Score, Accuracy, MaxCombo, (Mods)Mods, N300, N100, N50,
                NMiss, NGeki, NKatu, Grade, (GameMode)Mode, PlayTime, TimeElapsed, (ClientFlags)ClientFlags,
                UserId, Perfect, OnlineChecksum, SubmittedAt, IsInvalidated);
        }
    }
}
