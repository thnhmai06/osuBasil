using Basil.Domain.Beatmaps;

namespace Basil.Application.Abstractions.Beatmaps;

/// <summary>
///     Ported from app/repositories/maps.py's MapsRepository, scoped to what beatmap resolution
///     needs: lookup by md5/id/filename and upsert. The `server` column (osu!/private) is
///     hardcoded to "osu!" — the private-server-map feature it models has no other code path
///     anywhere in bancho.py that ever sets it to "private".
/// </summary>
public interface IMapRepository
{
    /// <summary>
    ///     At least one of id/md5/filename/setId must be provided, matching MapsRepository.fetch_one
    ///     (setId added on top of the Python source's fetch_one — it's how osu-search-set.php's "any
    ///     map in this set" lookup is served, mirroring fetch_set_info without a separate DTO/method).
    ///     When multiple maps share a setId, an arbitrary one among them is returned.
    ///     <paramref name="includeFrozen" /> defaults to false (a frozen beatmap is hidden from the
    ///     client entirely) — only admin-key-gated routes and internal ingestion/upsert plumbing
    ///     that must see a frozen row to update it in place should pass true.
    /// </summary>
    Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null, int? setId = null,
        bool includeFrozen = false, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Ported from BeatmapSet._save_to_sql's REPLACE INTO semantics for a single map, plus id
    ///     resolution: matched by <see cref="Beatmap.Md5" /> first (existing row keeps its own Id
    ///     regardless of what's passed in); otherwise <paramref name="beatmap" />'s `Id` is used
    ///     directly if positive (a real osu! online id), or resolved from
    ///     <c>Math.Max(Beatmap.LocalIdFloor, FetchMaxIdAsync() + 1)</c> when `Id` is 0. Returns the
    ///     row as actually persisted, with its resolved Id.
    /// </summary>
    Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default);

    /// <summary>Ported from the map_md5s_to_delete cascade in BeatmapSet._update_if_available.</summary>
    Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Ported from DirectSearchService.search, replumbed to query the local `maps` table instead
    ///     of proxying a mirror API (osu-search.php now runs fully offline). Returns beatmap sets
    ///     (grouped by set_id, newest set_id first) each already ordered by star rating ascending,
    ///     matching the mirror-response shape the Python source consumed. Always excludes frozen
    ///     beatmaps — a discovery surface, not a specific-row lookup.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
        string? query, GameMode? mode, int offset, int amount,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Ported from apply_beatmap_play_stats. A delta update rather than Python's read-modify-write
    ///     (Python mutates the in-memory cached Beatmap.plays/.passes then writes the result back;
    ///     Basil resolves a fresh Beatmap per-request instead of caching one, so a delta avoids a
    ///     lost-update race between concurrent submissions on the same map).
    /// </summary>
    Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default);

    /// <summary>
    ///     The highest Id currently in use (0 if the table is empty),
    ///     used to allocate local ids for beatmaps whose .osu file carries no real online id.
    /// </summary>
    Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Caches a freshly-computed star rating onto a beatmap row for /difficulty-rating.</summary>
    Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Every non-frozen beatmap sharing a set, used by /d/{setId}'s on-the-fly .osz packaging
    ///     (ships file bytes to the client, so frozen beatmaps are always excluded) and by ingestion
    ///     reconciliation's disk-diff (which needs every beatmap regardless of frozen status —
    ///     ingestion passes <paramref name="includeFrozen" /> true for that internal use).
    /// </summary>
    Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includeFrozen = false,
        CancellationToken cancellationToken = default);
}