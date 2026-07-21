using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Application.Abstractions.Multiplayer;

/// <summary>
///     Persists the Matches/Rounds/MatchEvents tables — the durable record of a multiplayer room
///     (1 room = 1 Match) and each beatmap played within it (1 beatmap = 1 Round). WinningTeam is
///     intentionally not persisted here — see Rounds' schema comment in 001_base.sql for why it's
///     computed on read instead.
/// </summary>
public interface IMatchPersistenceRepository
{
    /// <summary>Returns the newly created Matches.Id.</summary>
    Task<int> CreateMatchAsync(
        string name, DateTime createdAt, CancellationToken cancellationToken = default);

    Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default);

    /// <summary>Returns the newly created Rounds.Id.</summary>
    Task<int> CreateRoundAsync(
        int matchId, int roundIndex, int beatmapId, string mapMd5,
        GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
        string beatmapArtist, string beatmapTitle, string beatmapVersion, string beatmapCreator,
        Mods mods, DateTime startedAt,
        CancellationToken cancellationToken = default);

    Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted,
        CancellationToken cancellationToken = default);

    /// <summary>New for MatchReportService (the TRT builder) — null when no such match exists.</summary>
    Task<MatchRow?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default);

    /// <summary>New for MatchReportService. Ordered by RoundIndex ascending.</summary>
    Task<IReadOnlyList<RoundRow>> FetchRoundsAsync(int matchId, CancellationToken cancellationToken = default);

    /// <summary>New for the management REST API's match listing/deletion.</summary>
    Task<IReadOnlyList<MatchRow>> FetchAllMatchesAsync(CancellationToken cancellationToken = default);

    /// <summary>New for the management REST API — cascades to the match's Rounds first (FK).</summary>
    Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default);

    /// <summary>Log a match lifecycle event.</summary>
    Task CreateEventAsync(MatchEventRow row, CancellationToken cancellationToken = default);

    /// <summary>Fetch all events for a match, ordered by Timestamp ascending.</summary>
    Task<IReadOnlyList<MatchEventRow>> FetchEventsAsync(int matchId, CancellationToken cancellationToken = default);

    /// <summary>Find matches that weren't properly closed (server crash / shutdown).</summary>
    Task<IReadOnlyList<MatchRow>> FetchUnrecoveredMatchesAsync(CancellationToken cancellationToken = default);

    /// <summary>Find rounds that weren't properly ended within a match.</summary>
    Task<IReadOnlyList<RoundRow>> FetchUnrecoveredRoundsAsync(int matchId, CancellationToken cancellationToken = default);
}

/// <summary>New for MatchReportService/management API reads — a raw Matches row.</summary>
public sealed record MatchRow(
    int Id,
    string Name,
    DateTime CreatedAt,
    DateTime? EndedAt);

/// <summary>New for MatchReportService — a raw Rounds row.</summary>
public sealed record RoundRow(
    int Id,
    int MatchId,
    int RoundIndex,
    int BeatmapId,
    string MapMd5,
    GameMode Mode,
    MatchWinCondition WinCondition,
    MatchTeamType TeamType,
    string BeatmapArtist,
    string BeatmapTitle,
    string BeatmapVersion,
    string BeatmapCreator,
    bool Aborted,
    Mods Mods,
    DateTime StartedAt,
    DateTime? EndedAt);
