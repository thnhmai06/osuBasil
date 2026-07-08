using Basil.Domain.Beatmaps;

namespace Basil.Application.UseCases.Beatmaps;

/// <summary>
///     Ported from app/api/domains/osu.py's format_direct_search_response (DIRECT_SET_INFO_FMTSTR /
///     DIRECT_MAP_INFO_FMTSTR). No golden fixture exists in bancho.py for this response — same
///     hand-built-from-format-string caveat as GetScoresResponseFormatter.
/// </summary>
public static class DirectSearchResponseFormatter
{
    public static string Format(IReadOnlyList<IReadOnlyList<Beatmap>> beatmapSets)
    {
        // Ported from DirectSearchService.search: a full page signals "there may be more" to the
        // client, so it's reported as 101 rather than the literal count.
        var resultCount = beatmapSets.Count == DirectSearchService.PageSize ? 101 : beatmapSets.Count;
        var lines = new List<string> { resultCount.ToString() };

        foreach (var set in beatmapSets)
        {
            var first = set[0];
            var diffs = string.Join(",", set.Select(FormatDiff));
            lines.Add(string.Join('|',
                $"{first.SetId}.osz",
                RemovePipes(first.Artist),
                RemovePipes(first.Title),
                first.Creator,
                first.Status.OsuApi().ToString(),
                "10.0",
                first.LastUpdate.ToString("yyyy-MM-dd HH:mm:ss"),
                first.SetId.ToString(),
                "0", "0", "0", "0", "0",
                diffs));
        }

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
            $"{beatmapSet.SetId}.osz",
            beatmapSet.Artist,
            beatmapSet.Title,
            beatmapSet.Creator,
            ((int)beatmapSet.Status).ToString(),
            "10.0",
            beatmapSet.LastUpdate.ToString("yyyy-MM-dd HH:mm:ss"),
            beatmapSet.SetId.ToString(),
            "0", "0", "0", "0", "0");
    }

    private static string FormatDiff(Beatmap beatmap)
    {
        return $"[{beatmap.Diff:0.00}⭐] {RemovePipes(beatmap.Version)} " +
               $"{{cs: {beatmap.Cs} / od: {beatmap.Od} / ar: {beatmap.Ar} / hp: {beatmap.Hp}}}@{(int)beatmap.Mode}";
    }

    // Ported from DirectSearchService._replace_osudirect_delimiter — "|" is the field delimiter
    // in this response format, so any literal "|" in metadata would corrupt it.
    private static string RemovePipes(string value)
    {
        return value.Replace('|', 'I');
    }
}