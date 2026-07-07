namespace OpenOsuTournament.Bancho.Application.Abstractions.Multiplayer;

/// <summary>
///     Persists the Matches/Rounds tables — the durable record of a multiplayer room (1 room = 1
///     Match) and each beatmap played within it (1 beatmap = 1 Round). Distinct from
///     <c>IMatchRegistry</c>, which holds the live in-memory <c>MatchSession</c> while a room is
///     open. WinningTeam is intentionally not persisted here — see Rounds' schema comment in
///     001_base.sql for why it's computed on read instead.
/// </summary>
public interface IMatchPersistenceRepository
{
    /// <summary>Returns the newly created Matches.Id.</summary>
    Task<int> CreateMatchAsync(
        string name, int mode, int winCondition, int teamType, int hostId, bool hasPublicHistory,
        DateTime createdAt, CancellationToken cancellationToken = default);

    Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default);

    /// <summary>Returns the newly created Rounds.Id.</summary>
    Task<int> CreateRoundAsync(
        int matchId, int roundIndex, int beatmapId, string mapMd5, int mods, DateTime startedAt,
        CancellationToken cancellationToken = default);

    Task SetRoundEndedAsync(int roundId, DateTime endedAt, CancellationToken cancellationToken = default);

    /// <summary>New for MatchReportService (the TRT builder) — null when no such match exists.</summary>
    Task<MatchRow?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default);

    /// <summary>New for MatchReportService. Ordered by RoundIndex ascending.</summary>
    Task<IReadOnlyList<RoundRow>> FetchRoundsAsync(int matchId, CancellationToken cancellationToken = default);

    /// <summary>New for the management REST API's match listing/deletion.</summary>
    Task<IReadOnlyList<MatchRow>> FetchAllMatchesAsync(CancellationToken cancellationToken = default);

    /// <summary>New for the management REST API — cascades to the match's Rounds first (FK).</summary>
    Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default);
}

/// <summary>New for MatchReportService/management API reads — a raw Matches row.</summary>
public sealed record MatchRow(
    int Id, string Name, int Mode, int WinCondition, int TeamType, int HostId, bool HasPublicHistory,
    DateTime CreatedAt, DateTime? EndedAt);

/// <summary>New for MatchReportService — a raw Rounds row.</summary>
public sealed record RoundRow(
    int Id, int MatchId, int RoundIndex, int BeatmapId, string MapMd5, int Mods, DateTime StartedAt,
    DateTime? EndedAt);
