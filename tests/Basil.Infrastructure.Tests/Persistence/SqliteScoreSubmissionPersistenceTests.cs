using Basil.Application.Abstractions.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Repositories;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Ported from bancho.py's `async with self.database.transaction():` wrapping the previous-best
///     demotion and score insert in ScoreSubmissionService — both commit (or fail) together, unlike
///     calling repositories separately over independent connections. Unlike bancho.py, there is no
///     stats update in the same transaction — stats are fixed (see docs/scope-decisions.md).
/// </summary>
public class SqliteScoreSubmissionPersistenceTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private readonly SqliteScoreSubmissionPersistence _persistence = new(fixture.ConnectionString);

    private async Task InsertUserAsync(int id, string name)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO Users (Id, Name, SafeName, PwBcrypt, Priv, Country, CreationTime, LatestActivity)
            VALUES (@Id, @Name, @Name, 'unused', @Priv, 'xx', unixepoch(), unixepoch())
            """,
            new { Id = id, Name = name, Priv = (int)Privileges.Unrestricted });
    }

    private async Task<long> InsertScoreAsync(string mapMd5, int userId, long score, SubmissionStatus status)
    {
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO Scores (
                MapMd5, Score, Acc, MaxCombo, Mods, N300, N100, N50, NMiss, NGeki, NKatu,
                Grade, Status, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect, OnlineChecksum,
                SubmittedAt
            ) VALUES (
                @MapMd5, @Score, 95.0, 500, 0, 300, 10, 5, 0, 0, 0,
                'S', @Status, @Mode, datetime('now'), 120000, 0, @UserId, 0, @Checksum,
                datetime('now')
            );
            SELECT last_insert_rowid();
            """,
            new
            {
                MapMd5 = mapMd5, Score = score, Status = (int)status, Mode = (int)GameMode.VanillaOsu, UserId = userId,
                Checksum = Guid.NewGuid().ToString("N")
            });
    }

    private static ScoreInsertRow MakeInsertRow(string mapMd5, int userId, long score, string checksum)
    {
        return new ScoreInsertRow(
            mapMd5, score, 98.5, 500, 0,
            300, 10, 5, 0, 0, 0,
            "S", (int)SubmissionStatus.Best, (int)GameMode.VanillaOsu,
            DateTime.UtcNow, 120000, 0, userId,
            false, checksum, DateTime.UtcNow);
    }

    [Fact]
    public async Task PersistScoreSubmission_InsertsScore()
    {
        var mapMd5 = new string('1', 32);
        await InsertUserAsync(401, "alice");
        var checksum = Guid.NewGuid().ToString("N");

        var scoreId = await _persistence.PersistScoreSubmissionAsync(
            false, mapMd5, 401, GameMode.VanillaOsu, MakeInsertRow(mapMd5, 401, 500_000, checksum));

        Assert.True(scoreId > 0);

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        var storedScore =
            await connection.ExecuteScalarAsync<long>("SELECT Score FROM Scores WHERE Id = @Id", new { Id = scoreId });
        Assert.Equal(500_000, storedScore);
    }

    [Fact]
    public async Task PersistScoreSubmission_MarksPreviousBestSubmitted()
    {
        var mapMd5 = new string('2', 32);
        await InsertUserAsync(402, "bob");
        var previousScoreId = await InsertScoreAsync(mapMd5, 402, 400_000, SubmissionStatus.Best);
        var checksum = Guid.NewGuid().ToString("N");

        await _persistence.PersistScoreSubmissionAsync(
            true, mapMd5, 402, GameMode.VanillaOsu, MakeInsertRow(mapMd5, 402, 500_000, checksum));

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        var previousStatus = await connection.ExecuteScalarAsync<int>("SELECT Status FROM Scores WHERE Id = @Id",
            new { Id = previousScoreId });
        Assert.Equal((int)SubmissionStatus.Submitted, previousStatus);
    }

    [Fact]
    public async Task PersistScoreSubmission_FailureOnInsert_RollsBackTheDemotionInTheSameTransaction()
    {
        var mapMd5 = new string('3', 32);
        await InsertUserAsync(403, "carol");
        var previousScoreId = await InsertScoreAsync(mapMd5, 403, 400_000, SubmissionStatus.Best);
        // RoundId references a nonexistent Round — violates Scores_Rounds_Id_fk (Foreign
        // Keys=True is on for every connection), forcing a failure on the score-insert statement
        // (which runs after the previous-best demotion in the same transaction).
        var invalidRow = MakeInsertRow(mapMd5, 403, 500_000, Guid.NewGuid().ToString("N")) with { RoundId = 999_999 };

        await Assert.ThrowsAsync<SqliteException>(() => _persistence.PersistScoreSubmissionAsync(
            true, mapMd5, 403, GameMode.VanillaOsu, invalidRow));

        await using var connection = new SqliteConnection(fixture.ConnectionString);
        var previousStatus = await connection.ExecuteScalarAsync<int>("SELECT Status FROM Scores WHERE Id = @Id",
            new { Id = previousScoreId });
        Assert.Equal((int)SubmissionStatus.Best, previousStatus); // demotion rolled back, not left as Submitted
    }
}