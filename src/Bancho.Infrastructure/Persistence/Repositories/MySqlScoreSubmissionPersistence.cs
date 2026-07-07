using Bancho.Application.Abstractions.Scores;
using Bancho.Domain.Beatmaps;
using Bancho.Domain.Scores;
using Dapper;
using MySqlConnector;

namespace Bancho.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IScoreSubmissionPersistence" />
public sealed class MySqlScoreSubmissionPersistence(string connectionString) : IScoreSubmissionPersistence
{
    private readonly string _connectionString = connectionString + ";TreatTinyAsBoolean=false";

    public async Task<long> PersistScoreSubmissionAsync(
        bool markPreviousBestSubmitted,
        string mapMd5,
        int userId,
        GameMode mode,
        ScoreInsertRow scoreRow,
        StatsUpdateRow statsUpdate,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (markPreviousBestSubmitted)
            await connection.ExecuteAsync(
                """
                UPDATE scores SET status = @Submitted
                WHERE status = @Best AND map_md5 = @MapMd5 AND userid = @UserId AND mode = @Mode
                """,
                new
                {
                    Submitted = (int)SubmissionStatus.Submitted, Best = (int)SubmissionStatus.Best,
                    MapMd5 = mapMd5, UserId = userId, Mode = (int)mode
                },
                transaction);

        var scoreId = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO scores
                (map_md5, score, pp, acc, max_combo, mods, n300, n100, n50, nmiss, ngeki, nkatu,
                 grade, status, mode, play_time, time_elapsed, client_flags, userid, perfect, online_checksum)
            VALUES
                (@MapMd5, @Score, 0, @Acc, @MaxCombo, @Mods, @N300, @N100, @N50, @NMiss, @NGeki, @NKatu,
                 @Grade, @Status, @Mode, @PlayTime, @TimeElapsed, @ClientFlags, @UserId, @Perfect, @OnlineChecksum);
            SELECT LAST_INSERT_ID();
            """,
            scoreRow,
            transaction);

        await connection.ExecuteAsync(
            """
            UPDATE stats
            SET tscore = @Tscore, rscore = @Rscore, plays = @Plays, playtime = @Playtime,
                acc = @Acc, max_combo = @MaxCombo, total_hits = @TotalHits,
                xh_count = @XhCount, x_count = @XCount, sh_count = @ShCount, s_count = @SCount, a_count = @ACount
            WHERE id = @UserId AND mode = @Mode
            """,
            new
            {
                UserId = userId,
                Mode = (int)mode,
                statsUpdate.Tscore,
                statsUpdate.Rscore,
                statsUpdate.Plays,
                statsUpdate.Playtime,
                statsUpdate.Acc,
                statsUpdate.MaxCombo,
                statsUpdate.TotalHits,
                statsUpdate.XhCount,
                statsUpdate.XCount,
                statsUpdate.ShCount,
                statsUpdate.SCount,
                statsUpdate.ACount
            },
            transaction);

        await transaction.CommitAsync(cancellationToken);
        return scoreId;
    }
}