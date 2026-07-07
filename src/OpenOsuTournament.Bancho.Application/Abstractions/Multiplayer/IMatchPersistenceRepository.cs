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
}
