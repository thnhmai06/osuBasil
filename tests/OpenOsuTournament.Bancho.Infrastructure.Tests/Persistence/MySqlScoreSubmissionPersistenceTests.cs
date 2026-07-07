using Dapper;
using MySqlConnector;
using OpenOsuTournament.Bancho.Application.Abstractions.Scores;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Scores;
using OpenOsuTournament.Bancho.Domain.Users;
using OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

namespace OpenOsuTournament.Bancho.Infrastructure.Tests.Persistence;

/// <summary>
///     Ported from bancho.py's `async with self.database.transaction():` wrapping the previous-best
///     demotion and score insert in ScoreSubmissionService — both commit (or fail) together, unlike
///     calling repositories separately over independent connections. Unlike bancho.py, there is no
///     stats update in the same transaction — stats are fixed (see docs/scope-decisions.md).
/// </summary>
public class MySqlScoreSubmissionPersistenceTests : IClassFixture<MySqlFixture>
{
    private readonly MySqlFixture _fixture;
    private readonly MySqlScoreSubmissionPersistence _persistence;

    public MySqlScoreSubmissionPersistenceTests(MySqlFixture fixture)
    {
        _fixture = fixture;
        _persistence = new MySqlScoreSubmissionPersistence(fixture.ConnectionString);
    }

    private async Task InsertUserAsync(int id, string name)
    {
        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO Users (Id, Name, SafeName, Email, PwBcrypt, Priv, Country, CreationTime, LatestActivity)
            VALUES (@Id, @Name, @Name, @Email, 'unused', @Priv, 'xx', UNIX_TIMESTAMP(), UNIX_TIMESTAMP())
            """,
            new { Id = id, Name = name, Email = $"{name}@test.local", Priv = (int)Privileges.Unrestricted });
    }

    private async Task<long> InsertScoreAsync(string mapMd5, int userId, long score, SubmissionStatus status)
    {
        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO Scores (
                MapMd5, Score, Acc, MaxCombo, Mods, N300, N100, N50, NMiss, NGeki, NKatu,
                Grade, Status, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect, OnlineChecksum
            ) VALUES (
                @MapMd5, @Score, 95.0, 500, 0, 300, 10, 5, 0, 0, 0,
                'S', @Status, @Mode, NOW(), 120000, 0, @UserId, 0, @Checksum
            );
            SELECT LAST_INSERT_ID();
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
            false, checksum);
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

        await using var connection = new MySqlConnection(_fixture.ConnectionString);
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

        await using var connection = new MySqlConnection(_fixture.ConnectionString);
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
        // Grade is varchar(2) — this violates the column and throws under MySQL's default
        // STRICT_TRANS_TABLES sql_mode, forcing a failure on the score-insert statement (which
        // runs after the previous-best demotion in the same transaction).
        var invalidRow = MakeInsertRow(mapMd5, 403, 500_000, Guid.NewGuid().ToString("N")) with { Grade = "TOOLONG" };

        await Assert.ThrowsAsync<MySqlException>(() => _persistence.PersistScoreSubmissionAsync(
            true, mapMd5, 403, GameMode.VanillaOsu, invalidRow));

        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        var previousStatus = await connection.ExecuteScalarAsync<int>("SELECT Status FROM Scores WHERE Id = @Id",
            new { Id = previousScoreId });
        Assert.Equal((int)SubmissionStatus.Best, previousStatus); // demotion rolled back, not left as Submitted
    }
}
