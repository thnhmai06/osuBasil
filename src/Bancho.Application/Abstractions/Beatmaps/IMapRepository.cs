using Bancho.Domain.Beatmaps;

namespace Bancho.Application.Abstractions.Beatmaps;

/// <summary>
///     Ported from app/repositories/maps.py's MapsRepository, scoped to what beatmap resolution
///     (Phase 5) needs: lookup by md5/id/filename and upsert. The `server` column (osu!/private) is
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
    /// </summary>
    Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null, int? setId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Ported from BeatmapSet._save_to_sql's REPLACE INTO semantics for a single map.</summary>
    Task UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default);

    /// <summary>Ported from the map_md5s_to_delete cascade in BeatmapSet._update_if_available.</summary>
    Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Ported from DirectSearchService.search, replumbed to query the local `maps` table instead
    ///     of proxying a mirror API (osu-search.php now runs fully offline). Returns beatmap sets
    ///     (grouped by set_id, newest set_id first) each already ordered by star rating ascending,
    ///     matching the mirror-response shape the Python source consumed.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
        string? query, GameMode? mode, RankedStatus? status, int offset, int amount,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Ported from apply_beatmap_play_stats. A delta update rather than Python's read-modify-write
    ///     (Python mutates the in-memory cached Beatmap.plays/.passes then writes the result back;
    ///     bancho-net resolves a fresh Beatmap per-request instead of caching one, so a delta avoids a
    ///     lost-update race between concurrent submissions on the same map).
    /// </summary>
    Task IncrementPlayCountsAsync(int mapId, bool passed, CancellationToken cancellationToken = default);
}