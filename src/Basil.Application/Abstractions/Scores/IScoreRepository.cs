using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Application.Abstractions.Scores;

/// <summary>Ported from app/repositories/scores.py's FirstPlaceScore.</summary>
public sealed record FirstPlaceScoreRow(int Id, string Name);

/// <summary>The subset of a score row ReplayService.fetch_replay_file actually reads off `score.player`/`score.mode`.</summary>
public sealed record ScoreOwnerRow(int UserId, GameMode Mode);

/// <summary>Every score submitted within one Round, used by MatchReportService (the TRT builder).</summary>
public sealed record RoundScoreRow(
    long Id,
    int UserId,
    string UserName,
    MatchTeam? Team,
    Mods Mods,
    long Score,
    double Accuracy,
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
    double Accuracy,
    int MaxCombo,
    Mods Mods,
    int N300,
    int N100,
    int N50,
    int NMiss,
    int NGeki,
    int NKatu,
    string Grade,
    GameMode Mode,
    DateTime PlayTime,
    int TimeElapsed,
    ClientFlags ClientFlags,
    int UserId,
    bool Perfect,
    string OnlineChecksum,
    DateTime SubmittedAt,
    int? RoundId = null,
    MatchTeam? Team = null);

/// <summary>One score's full row, for the public `GET /score/{id}` route — includes `IsInvalidated`,
/// which the API surfaces as a flag rather than hiding the score entirely.</summary>
public sealed record ScoreRow(
    long Id,
    int? RoundId,
    MatchTeam? Team,
    string MapMd5,
    long Score,
    double Accuracy,
    int MaxCombo,
    Mods Mods,
    int N300,
    int N100,
    int N50,
    int NMiss,
    int NGeki,
    int NKatu,
    string Grade,
    GameMode Mode,
    DateTime PlayTime,
    int TimeElapsed,
    ClientFlags ClientFlags,
    int UserId,
    bool Perfect,
    string OnlineChecksum,
    DateTime SubmittedAt,
    bool IsInvalidated);

public interface IScoreRepository
{
    /// <summary>Ported from ScoresRepository.create. Returns the new row's auto-increment id.</summary>
    Task<long> CreateAsync(ScoreInsertRow row, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Ported from score_submission_is_duplicate's use of fetch_one_by_online_checksum — only the
    ///     existence check is used, so this skips deserializing a full row.
    /// </summary>
    Task<bool> ExistsByOnlineChecksumAsync(string onlineChecksum, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Ported from ScoresRepository.fetch_first_place_score, scoring_metric dropped (always score,
    ///     per Basil's no-pp scope).
    /// </summary>
    Task<FirstPlaceScoreRow?> FetchFirstPlaceScoreAsync(string mapMd5, GameMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>Ported from the `score.player`/`score.mode` reads in ReplayService.fetch_replay_file.</summary>
    Task<ScoreOwnerRow?> FetchOwnerAsync(long scoreId, CancellationToken cancellationToken = default);

    /// <summary>One score's full row, for the public `GET /score/{id}` route. Null if no score with this id exists.</summary>
    Task<ScoreRow?> FetchByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Newest-first page of every score row, for the public `GET /scores` list route.</summary>
    Task<IReadOnlyList<ScoreRow>> FetchPageAsync(int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>For MatchReportService (the TRT builder) — every score linked to one Round.</summary>
    Task<IReadOnlyList<RoundScoreRow>> FetchByRoundIdAsync(int roundId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Flags every score on <paramref name="mapMd5" /> as invalidated — never a hard delete. Called
    ///     by the beatmap ingestion cascade whenever a beatmap's content changes or its file/mapset
    ///     disappears, so a player's own historical record survives a metadata fix or map removal.
    /// </summary>
    Task InvalidateByMapMd5Async(string mapMd5, CancellationToken cancellationToken = default);
}
