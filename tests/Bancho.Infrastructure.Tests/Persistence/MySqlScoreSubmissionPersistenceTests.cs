using Bancho.Application.Abstractions.Scores;
using Bancho.Domain.Beatmaps;
using Bancho.Domain.Scores;
using Bancho.Domain.Users;
using Bancho.Infrastructure.Persistence.Repositories;
using Dapper;
using MySqlConnector;

namespace Bancho.Infrastructure.Tests.Persistence;

/// <summary>
///     Ported from bancho.py's `async with self.database.transaction():` wrapping the previous-best
///     demotion, score insert, and stats update in ScoreSubmissionService — all three commit (or fail)
///     together, unlike calling the three repositories separately over independent connections.
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

    private async Task InsertUserAndStatsAsync(int id, string name)
    {
        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO users (id, name, safe_name, email, pw_bcrypt, priv, country, creation_time, latest_activity)
            VALUES (@Id, @Name, @Name, @Email, 'unused', @Priv, 'xx', UNIX_TIMESTAMP(), UNIX_TIMESTAMP())
            """,
            new { Id = id, Name = name, Email = $"{name}@test.local", Priv = (int)Privileges.Unrestricted });

        await connection.ExecuteAsync(
            "INSERT INTO stats (id, mode) VALUES (@Id, @Mode)",
            new { Id = id, Mode = (int)GameMode.VanillaOsu });
    }

    private async Task<long> InsertScoreAsync(string mapMd5, int userId, long score, SubmissionStatus status)
    {
        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO scores (
                map_md5, score, pp, acc, max_combo, mods, n300, n100, n50, nmiss, ngeki, nkatu,
                grade, status, mode, play_time, time_elapsed, client_flags, userid, perfect, online_checksum
            ) VALUES (
                @MapMd5, @Score, 0, 95.0, 500, 0, 300, 10, 5, 0, 0, 0,
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

    private static StatsUpdateRow MakeStatsUpdate()
    {
        return new StatsUpdateRow(
            500_000, 500_000, 1, 60, 0, 500, 315,
            0, 0, 1, 0, 0);
    }

    [Fact]
    public async Task PersistScoreSubmission_InsertsScoreAndUpdatesStatsAtomically()
    {
        var mapMd5 = new string('1', 32);
        await InsertUserAndStatsAsync(401, "alice");
        var checksum = Guid.NewGuid().ToString("N");

        var scoreId = await _persistence.PersistScoreSubmissionAsync(
            false, mapMd5, 401, GameMode.VanillaOsu,
            MakeInsertRow(mapMd5, 401, 500_000, checksum), MakeStatsUpdate());

        Assert.True(scoreId > 0);

        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        var storedScore =
            await connection.ExecuteScalarAsync<long>("SELECT score FROM scores WHERE id = @Id", new { Id = scoreId });
        var storedRscore =
            await connection.ExecuteScalarAsync<long>("SELECT rscore FROM stats WHERE id = @Id AND mode = 0",
                new { Id = 401 });
        Assert.Equal(500_000, storedScore);
        Assert.Equal(500_000, storedRscore);
    }

    [Fact]
    public async Task PersistScoreSubmission_MarksPreviousBestSubmitted()
    {
        var mapMd5 = new string('2', 32);
        await InsertUserAndStatsAsync(402, "bob");
        var previousScoreId = await InsertScoreAsync(mapMd5, 402, 400_000, SubmissionStatus.Best);
        var checksum = Guid.NewGuid().ToString("N");

        await _persistence.PersistScoreSubmissionAsync(
            true, mapMd5, 402, GameMode.VanillaOsu,
            MakeInsertRow(mapMd5, 402, 500_000, checksum), MakeStatsUpdate());

        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        var previousStatus = await connection.ExecuteScalarAsync<int>("SELECT status FROM scores WHERE id = @Id",
            new { Id = previousScoreId });
        Assert.Equal((int)SubmissionStatus.Submitted, previousStatus);
    }

    [Fact]
    public async Task PersistScoreSubmission_FailureOnLaterStatement_RollsBackEarlierWritesInTheSameTransaction()
    {
        var mapMd5 = new string('3', 32);
        await InsertUserAndStatsAsync(403, "carol");
        var previousScoreId = await InsertScoreAsync(mapMd5, 403, 400_000, SubmissionStatus.Best);
        // grade is varchar(2) — this violates the column and throws under MySQL's default
        // STRICT_TRANS_TABLES sql_mode, forcing a failure on the score-insert statement (which
        // runs after the previous-best demotion in the same transaction).
        var invalidRow = MakeInsertRow(mapMd5, 403, 500_000, Guid.NewGuid().ToString("N")) with { Grade = "TOOLONG" };

        await Assert.ThrowsAsync<MySqlException>(() => _persistence.PersistScoreSubmissionAsync(
            true, mapMd5, 403, GameMode.VanillaOsu, invalidRow, MakeStatsUpdate()));

        await using var connection = new MySqlConnection(_fixture.ConnectionString);
        var previousStatus = await connection.ExecuteScalarAsync<int>("SELECT status FROM scores WHERE id = @Id",
            new { Id = previousScoreId });
        Assert.Equal((int)SubmissionStatus.Best, previousStatus); // demotion rolled back, not left as Submitted
    }
}