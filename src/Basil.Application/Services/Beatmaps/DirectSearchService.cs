using Basil.Application.Abstractions.Beatmaps;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Services.Beatmaps;

/// <summary>Ported from app/services/direct_search.py's DirectSearchParams/search signature.</summary>
public sealed record DirectSearchRequest(string Query, int Mode, int PageNum);

/// <summary>
///     Ported from app/services/direct_search.py's DirectSearchService, replumbed to query the local
///     `maps` table instead of proxying a mirror API (osu-search.php runs fully offline now), plus
///     app/api/domains/osu.py's format_direct_search_response formatting (folded in here since both
///     only exist to serve the same osu!direct panel). The mirror-error result code goes away with
///     the mirror — this server never talks to one. The "|"-in-metadata replacement quirk stays: it's
///     not mirror-specific, it protects the pipe-delimited wire format from any locally-stored
///     artist/title/diffname that happens to contain a literal "|".
/// </summary>
public sealed class DirectSearchService(IMapRepository maps)
{
    /// <summary>A full page signals "there may be more" to the client (reported as 101 rather than the literal count).</summary>
    private const int PageSize = 100;

    /// <summary>Client sentinel for "any mode" in <see cref="DirectSearchRequest.Mode" />.</summary>
    private const int AnyMode = -1;

    private static readonly string[] NonTextQueries = ["Newest", "Top+Rated", "Most+Played"];

    public async Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(
        DirectSearchRequest request, CancellationToken cancellationToken = default)
    {
        var queryText = NonTextQueries.Contains(request.Query) ? null : request.Query;
        GameMode? mode = request.Mode == AnyMode ? null : (GameMode)request.Mode;

        return await maps.SearchAsync(queryText, mode, request.PageNum * PageSize, PageSize,
            cancellationToken);
    }

    /// <summary>Ported from app/api/domains/osu.py's format_direct_search_response (DIRECT_SET_INFO_FMTSTR / DIRECT_MAP_INFO_FMTSTR).</summary>
    public static string Format(IReadOnlyList<IReadOnlyList<Beatmap>> beatmapSets)
    {
        // A full page signals "there may be more" to the client, so it's reported as 101 rather than the literal count.
        var resultCount = beatmapSets.Count == PageSize ? 101 : beatmapSets.Count;
        var lines = new List<string> { resultCount.ToString() };
        lines.AddRange(from set in beatmapSets
            let first = set[0]
            let diffs = string.Join(",", set.Select(FormatDiff))
            select string.Join('|', $"{first.Mapset.Id}.osz", RemovePipes(first.Mapset.Artist),
                RemovePipes(first.Mapset.Title), first.Mapset.Creator,
                first.Mapset.Status.ToOsuApi().ToString(), "10.0",
                first.Mapset.LastUpdate.ToString("yyyy-MM-dd HH:mm:ss"), first.Mapset.Id.ToString(), "0", "0", "0", "0",
                "0", diffs));

        return string.Join("\n", lines);
    }

    /// <summary>
    ///     Ported from osu.py's osuSearchSetHandler inline format string. Unlike Format above, this
    ///     does NOT escape "|" in metadata and reports RankedStatus using the server's own raw enum
    ///     value (not the osu!api-converted one) — both match the Python source exactly, which is
    ///     inconsistent with the search-listing endpoint's formatting but not a bug of ours to fix.
    /// </summary>
    public static string FormatSet(Beatmap? beatmapSet)
    {
        if (beatmapSet is null) return "";

        return string.Join('|',
            $"{beatmapSet.Mapset.Id}.osz",
            beatmapSet.Mapset.Artist,
            beatmapSet.Mapset.Title,
            beatmapSet.Mapset.Creator,
            ((int)beatmapSet.Mapset.Status).ToString(),
            "10.0",
            beatmapSet.Mapset.LastUpdate.ToString("yyyy-MM-dd HH:mm:ss"),
            beatmapSet.Mapset.Id.ToString(),
            "0", "0", "0", "0", "0");
    }

    private static string FormatDiff(Beatmap beatmap)
    {
        return $"[{beatmap.Difficulty.Sr:0.00}⭐] {RemovePipes(beatmap.Version)} " +
               $"{{cs: {beatmap.Difficulty.Cs} / od: {beatmap.Difficulty.Od} / ar: {beatmap.Difficulty.Ar} / " +
               $"hp: {beatmap.Difficulty.Hp}}}@{(int)beatmap.Difficulty.Mode}";
    }

    // "|" is the field delimiter in this response format, so any literal "|" in metadata would corrupt it.
    private static string RemovePipes(string value)
    {
        return value.Replace('|', 'I');
    }
}