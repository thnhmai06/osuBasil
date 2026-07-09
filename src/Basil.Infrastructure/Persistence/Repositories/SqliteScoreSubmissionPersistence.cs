using Basil.Application.Abstractions.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IScoreSubmissionPersistence" />
public sealed class SqliteScoreSubmissionPersistence(string connectionString) : IScoreSubmissionPersistence
{
    public async Task<long> PersistScoreSubmissionAsync(
        bool markPreviousBestSubmitted,
        string mapMd5,
        int userId,
        GameMode mode,
        ScoreInsertRow scoreRow,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (markPreviousBestSubmitted)
            await connection.ExecuteAsync(
                """
                UPDATE Scores SET Status = @Submitted
                WHERE Status = @Best AND MapMd5 = @MapMd5 AND UserId = @UserId AND Mode = @Mode
                """,
                new
                {
                    Submitted = (int)SubmissionStatus.Submitted, Best = (int)SubmissionStatus.Best,
                    MapMd5 = mapMd5, UserId = userId, Mode = (int)mode
                },
                transaction);

        var scoreId = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO Scores
                (MapMd5, Score, Acc, MaxCombo, Mods, N300, N100, N50, NMiss, NGeki, NKatu,
                 Grade, Status, Mode, PlayTime, TimeElapsed, ClientFlags, UserId, Perfect, OnlineChecksum, RoundId, Team)
            VALUES
                (@MapMd5, @Score, @Acc, @MaxCombo, @Mods, @N300, @N100, @N50, @NMiss, @NGeki, @NKatu,
                 @Grade, @Status, @Mode, @PlayTime, @TimeElapsed, @ClientFlags, @UserId, @Perfect, @OnlineChecksum, @RoundId, @Team);
            SELECT last_insert_rowid();
            """,
            scoreRow,
            transaction);

        await transaction.CommitAsync(cancellationToken);
        return scoreId;
    }
}
