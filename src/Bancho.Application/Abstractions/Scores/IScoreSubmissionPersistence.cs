using Bancho.Domain.Beatmaps;

namespace Bancho.Application.Abstractions.Scores;

/// <summary>
///     Ported from bancho.py's `async with self.database.transaction():` wrapping
///     persist_submitted_score + persist_score_submission_stats in ScoreSubmissionService — the
///     previous-best demotion, score insert, and stats update commit atomically. Without this, a
///     mid-write failure (e.g. the stats update throwing after the score insert already committed on
///     a separate connection) leaves the previous best demoted and the new score persisted as BEST
///     with stats never updated — a real gap the initial Phase 6 port had (see note.md).
/// </summary>
public interface IScoreSubmissionPersistence
{
    /// <summary>Returns the newly inserted score's id.</summary>
    Task<long> PersistScoreSubmissionAsync(
        bool markPreviousBestSubmitted,
        string mapMd5,
        int userId,
        GameMode mode,
        ScoreInsertRow scoreRow,
        StatsUpdateRow statsUpdate,
        CancellationToken cancellationToken = default);
}

/// <summary>Ported from the parameters of StatsRepository.partial_update's score-submission fields.</summary>
public sealed record StatsUpdateRow(
    long Tscore,
    long Rscore,
    int Plays,
    int Playtime,
    double Acc,
    int MaxCombo,
    int TotalHits,
    int XhCount,
    int XCount,
    int ShCount,
    int SCount,
    int ACount);