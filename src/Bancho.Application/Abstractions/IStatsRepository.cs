namespace Bancho.Application.Abstractions;

/// <summary>
/// Ported from app/repositories/stats.py's Stats dataclass (bancho-net: no `pp` column usage —
/// see docs/csharp-migration-plan.md §0).
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
/// Ported from app/repositories/stats.py's StatsRepository, scoped to what login and score
/// submission need.
/// </summary>
public interface IStatsRepository
{
    Task<IReadOnlyList<Stats>> FetchAllForUserAsync(int userId, CancellationToken cancellationToken = default);

    Task<Stats?> FetchOneAsync(int userId, int mode, CancellationToken cancellationToken = default);

    Task UpdateAfterScoreAsync(
        int userId,
        int mode,
        long tscore,
        long rscore,
        int plays,
        int playtime,
        double acc,
        int maxCombo,
        int totalHits,
        CancellationToken cancellationToken = default);
}
