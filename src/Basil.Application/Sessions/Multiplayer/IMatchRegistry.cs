namespace Basil.Application.Sessions.Multiplayer;

/// <summary>
///     Runtime multiplayer match registry. Ported from app/objects/collections.py's Matches — a
///     fixed 64-slot table (osu!'s tourney-client packets validate `0 &lt;= match_id &lt; 64`), not an
///     unbounded collection.
/// </summary>
public interface IMatchRegistry
{
    IReadOnlyList<MatchSession> All { get; }
    MatchSession? GetById(int id);

    /// <summary>
    ///     New for the api. host's /multi/{id} routes, which key by the persistent Matches.Id
    ///     (<see cref="MatchSession.DbId" />) rather than the 0-63 wire-protocol slot <see cref="GetById" /> uses.
    /// </summary>
    MatchSession? GetByDbId(int dbId);

    /// <summary>
    ///     Atomically finds the first free slot (0..63) and registers the session <paramref name="factory" />
    ///     builds for that id, mirroring Matches.get_free immediately followed by assignment in
    ///     MatchCreate.handle — those two steps must not be separated by another thread's create.
    ///     Returns null if all slots are occupied.
    /// </summary>
    MatchSession? TryCreate(Func<int, MatchSession> factory);

    /// <summary>Ported from Matches.remove.</summary>
    void Remove(int id);
}