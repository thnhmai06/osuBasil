namespace Basil.Application.Abstractions.Users;

/// <summary>
///     Fixed gameplay stats — seeded once, never updated after score submission (server does not
///     track singleplayer ranking/progression). Ported from app/repositories/stats.py's Stats
///     dataclass (Basil: no `pp` column usage).
/// </summary>
public sealed record Stats(
    int Id,
    int Mode,
    long Tscore,
    long Rscore,
    int Plays,
    int Playtime,
    double Acc,
    int MaxCombo,
    int TotalHits,
    int ReplayViews,
    int XhCount,
    int XCount,
    int ShCount,
    int SCount,
    int ACount);

/// <summary>
///     Ported from app/repositories/stats.py's StatsRepository, scoped to what login and replay
///     viewing need — no score-submission update path (stats are fixed).
/// </summary>
public interface IStatsRepository
{
    Task<IReadOnlyList<Stats>> FetchAllForUserAsync(int userId, CancellationToken cancellationToken = default);

    Task<Stats?> FetchOneAsync(int userId, int mode, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Ported from Score.increment_replay_views — a plain delta update, kept separate from any
    ///     stats-update path because it targets the replay's owner, not necessarily the score
    ///     submitter (see ReplayService.fetch_replay_file's viewer_id != score.player.id check).
    /// </summary>
    Task IncrementReplayViewsAsync(int userId, int mode, CancellationToken cancellationToken = default);
}