using OpenOsuTournament.Bancho.Domain.Beatmaps;

namespace OpenOsuTournament.Bancho.Application.Abstractions.Scores;

/// <summary>
///     The previous-best demotion and score insert commit atomically, so a mid-write failure can't
///     leave the previous best demoted with the new score never persisted. Stats are fixed (no
///     progression tracking), so unlike bancho.py's ScoreSubmissionService this does not also write
///     a stats update in the same transaction.
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
        CancellationToken cancellationToken = default);
}
