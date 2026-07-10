using Basil.Domain.Beatmaps;

namespace Basil.Application.Abstractions.Scores;

/// <summary>
///     Ported from app/repositories/scores.py's BeatmapLeaderboardScoreRow. The Python dataclass calls
///     this field `leaderboard_value` because it's pp for rx/ap and score for vanilla — Basil's
///     no-pp scope decision means it is unconditionally the `score` column, so it's named plainly.
///     Clan tag prefixing (`[tag] name`) is not implemented; Name is the plain player name.
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

/// <summary>
///     Ported from app/repositories/scores.py's PersonalBestLeaderboardScoreRow. `Grade` was added on
///     top of the Python source's field set (which selects it separately, via `Score.from_sql`, only
///     when score submission's calculate_status needs it for grade_count_deltas) — defaulted so the
///     getscores read path's existing call sites are unaffected.
/// </summary>
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
    long Time,
    string Grade = "N",
    double Acc = 0.0);

/// <summary>Ported from app/repositories/scores.py's FirstPlaceScore.</summary>
public sealed record FirstPlaceScoreRow(int Id, string Name);

/// <summary>The subset of a score row ReplayService.fetch_replay_file actually reads off `score.player`/`score.mode`.</summary>
public sealed record ScoreOwnerRow(int UserId, GameMode Mode);

/// <summary>Every score submitted within one Round, used by MatchReportService (the TRT builder).</summary>
public sealed record RoundScoreRow(
    long Id,
    int UserId,
    string UserName,
    int? Team,
    int Mods,
    long Score,
    double Acc,
    int MaxCombo,
    int N300,
    int N100,
    int N50,
    int NMiss,
    int NGeki,
    int NKatu,
    string Grade,
    bool Perfect,
    DateTime SubmittedAt);

/// <summary>
///     Ported from the parameters of ScoresRepository.create — `pp` is deliberately absent, the
///     insert always writes 0 for it (no-pp scope), matching the plan's decision for the now-inert
///     scores.pp column.
/// </summary>
public sealed record ScoreInsertRow(
    string MapMd5,
    long Score,
    double Acc,
    int MaxCombo,
    int Mods,
    int N300,
    int N100,
    int N50,
    int NMiss,
    int NGeki,
    int NKatu,
    string Grade,
    int Status,
    int Mode,
    DateTime PlayTime,
    int TimeElapsed,
    int ClientFlags,
    int UserId,
    bool Perfect,
    string OnlineChecksum,
    DateTime SubmittedAt,
    int? RoundId = null,
    int? Team = null);

/// <summary>
///     Ported from app/repositories/scores.py's ScoresRepository, scoped to the three leaderboard
///     reads the getscores endpoint needs. `scoring_metric` is dropped entirely from every method's
///     signature (compared to the Python source) — Basil always ranks by raw score, never pp.
/// </summary>
public interface IScoreRepository
{
    /// <summary>
    ///     Ported from fetch_beatmap_leaderboard_scores. Only unrestricted players' scores are
    ///     visible, except the requesting player's own row is always included.
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

    /// <summary>Ported from ScoresRepository.create. Returns the new row's auto-increment id.</summary>
    Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Ported from score_submission_is_duplicate's use of fetch_one_by_online_checksum — only the
    ///     existence check is used, so this skips deserializing a full row.
    /// </summary>
    Task<bool> ExistsByOnlineChecksumAsync(string onlineChecksum, CancellationToken cancellationToken = default);

    /// <summary>Ported from ScoresRepository.mark_previous_best_scores_submitted.</summary>
    Task MarkPreviousBestScoresSubmittedAsync(string mapMd5, int userId, GameMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Ported from ScoresRepository.fetch_first_place_score, scoring_metric dropped (always score,
    ///     per Basil's no-pp scope).
    /// </summary>
    Task<FirstPlaceScoreRow?> FetchFirstPlaceScoreAsync(string mapMd5, GameMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>Ported from the `score.player`/`score.mode` reads in ReplayService.fetch_replay_file.</summary>
    Task<ScoreOwnerRow?> FetchOwnerAsync(long scoreId, CancellationToken cancellationToken = default);

    /// <summary>For MatchReportService (the TRT builder) — every score linked to one Round.</summary>
    Task<IReadOnlyList<RoundScoreRow>> FetchByRoundIdAsync(int roundId, CancellationToken cancellationToken = default);
}