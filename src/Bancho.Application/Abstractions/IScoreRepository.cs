using Bancho.Domain;

namespace Bancho.Application.Abstractions;

/// <summary>
/// Ported from app/repositories/scores.py's BeatmapLeaderboardScoreRow. The Python dataclass calls
/// this field `leaderboard_value` because it's pp for rx/ap and score for vanilla — bancho-net's
/// no-pp scope decision means it is unconditionally the `score` column, so it's named plainly.
/// Clan tag prefixing (`[tag] name`) is deferred until clans exist; Name is the plain player name.
/// </summary>
public sealed record BeatmapLeaderboardScoreRow(
    long Id,
    long Score,
    int MaxCombo,
    int N50,
    int N100,
    int N300,
    int NMiss,
    int NKatu,
    int NGeki,
    bool Perfect,
    int Mods,
    long Time,
    int UserId,
    string Name);

/// <summary>Ported from app/repositories/scores.py's PersonalBestLeaderboardScoreRow.</summary>
public sealed record PersonalBestLeaderboardScoreRow(
    long Id,
    long Score,
    int MaxCombo,
    int N50,
    int N100,
    int N300,
    int NMiss,
    int NKatu,
    int NGeki,
    bool Perfect,
    int Mods,
    long Time);

/// <summary>
/// Ported from app/repositories/scores.py's ScoresRepository, scoped to the three leaderboard
/// reads the getscores endpoint needs. `scoring_metric` is dropped entirely from every method's
/// signature (compared to the Python source) — bancho-net always ranks by raw score, never pp.
/// </summary>
public interface IScoreRepository
{
    /// <summary>
    /// Ported from fetch_beatmap_leaderboard_scores. Only unrestricted players' scores are
    /// visible, except the requesting player's own row is always included.
    /// </summary>
    Task<IReadOnlyList<BeatmapLeaderboardScoreRow>> FetchBeatmapLeaderboardScoresAsync(
        string mapMd5,
        GameMode mode,
        int userId,
        int? mods = null,
        IReadOnlySet<int>? friendIds = null,
        string? country = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<PersonalBestLeaderboardScoreRow?> FetchPersonalBestLeaderboardScoreAsync(
        string mapMd5,
        GameMode mode,
        int userId,
        CancellationToken cancellationToken = default);

    /// <summary>Ported from fetch_personal_best_leaderboard_rank — count of unrestricted scores strictly above, plus one.</summary>
    Task<int> FetchPersonalBestLeaderboardRankAsync(
        string mapMd5,
        GameMode mode,
        long score,
        CancellationToken cancellationToken = default);
}
