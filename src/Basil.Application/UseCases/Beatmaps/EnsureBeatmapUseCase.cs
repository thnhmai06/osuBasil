using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;

namespace Basil.Application.UseCases.Beatmaps;

public enum BeatmapResolutionResultCode
{
    Found,
    NeedsUpdate,
    NotSubmitted
}

public sealed record BeatmapResolutionResult(BeatmapResolutionResultCode Code, Beatmap? Beatmap);

/// <summary>
///     Ported from app/objects/beatmap.py's Beatmap.from_md5 + app/services/beatmap_leaderboards.py's
///     _classify_missing_beatmap, collapsed to DB-only resolution — this server has no osu!api
///     fallback (runs fully offline), so beatmaps must already exist in the local `maps` table.
///     Dropped entirely along with the osu!api fallback: the in-memory unsubmitted/needs-update
///     caches (app/state/cache.py) and the whole-set staleness-recheck machinery in BeatmapSet — both
///     existed solely "to reduce osu!api spam" (per the Python source's own comment), which is moot
///     with no osu!api calls to reduce. Every request just re-checks the DB, so an admin inserting a
///     new map is picked up on the very next request (Python needed a server restart for the same).
/// </summary>
public sealed class EnsureBeatmapUseCase(IMapRepository maps)
{
    public async Task<BeatmapResolutionResult> ResolveAsync(string md5, string filename,
        CancellationToken cancellationToken = default)
    {
        var bmap = await maps.FetchOneAsync(md5: md5, cancellationToken: cancellationToken);
        if (bmap is not null) return new BeatmapResolutionResult(BeatmapResolutionResultCode.Found, bmap);

        var existsByFilename =
            await maps.FetchOneAsync(filename: filename, cancellationToken: cancellationToken) is not null;
        return new BeatmapResolutionResult(
            existsByFilename ? BeatmapResolutionResultCode.NeedsUpdate : BeatmapResolutionResultCode.NotSubmitted,
            null);
    }
}