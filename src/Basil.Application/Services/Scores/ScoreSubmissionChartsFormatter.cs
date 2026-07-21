using System.Globalization;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Application.Services.Scores;

/// <summary>
///     Ported from app/api/domains/osu.py's chart_entry/build_submission_charts. Achievements are
///     dropped entirely (per scope) — the achievements-new field is always empty. `pp` chart entries
///     are always passed 0 (no-pp scope); chart_entry's own "0 or ''" falsy formatting empties them
///     automatically, so the field is kept (preserving the protocol's key/value shape for the osu!
///     client's result-screen parser) without any pp-specific special-casing. The "overall" (profile
///     stats) section has no before/after delta to show — stats are fixed, not updated on submission
///     (see docs/scope-decisions.md) — so every overall entry is emitted empty rather than fetching
///     stats that would never have actually changed.
/// </summary>
public static class ScoreSubmissionChartsFormatter
{
    public static string Format(ScoreSubmission score, Beatmap beatmap, long scoreId, int? rank, string domain)
    {
        var beatmapEntries = new[]
        {
            ChartEntry("rank", null, rank),
            ChartEntry("rankedScore", null, score.Score),
            ChartEntry("totalScore", null, score.Score),
            ChartEntry("maxCombo", null, score.MaxCombo),
            ChartEntry("accuracy", null, Math.Round(score.Accuracy, 2)),
            ChartEntry("pp", null, 0)
        };

        var overallEntries = new[]
        {
            ChartEntry("rank", null, null),
            ChartEntry("rankedScore", null, null),
            ChartEntry("totalScore", null, null),
            ChartEntry("maxCombo", null, null),
            ChartEntry("accuracy", null, null),
            ChartEntry("pp", 0, 0)
        };

        var parts = new List<string>
        {
            $"beatmapId:{beatmap.Id}",
            $"beatmapSetId:{beatmap.Mapset.Id}",
            $"beatmapPlaycount:{beatmap.Plays}",
            $"beatmapPasscount:{beatmap.Passes}",
            $"approvedDate:{beatmap.Mapset.LastUpdate:yyyy-MM-dd HH:mm:ss}",
            "\n",
            "chartId:beatmap",
            $"chartUrl:https://osu.{domain}/s/{beatmap.Mapset.Id}",
            "chartName:Beatmap Ranking"
        };
        parts.AddRange(beatmapEntries);
        parts.Add($"onlineScoreId:{scoreId}");
        parts.Add("\n");
        parts.Add("chartId:overall");
        parts.Add($"chartUrl:https://{domain}/u/{score.UserId}");
        parts.Add("chartName:Overall Ranking");
        parts.AddRange(overallEntries);
        parts.Add("achievements-new:");

        return string.Join('|', parts);
    }

    private static string ChartEntry(string name, object? before, object? after)
    {
        return $"{name}Before:{FormatValue(before)}|{name}After:{FormatValue(after)}";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "",
            int and 0 => "",
            long and 0 => "",
            double and 0.0 => "",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }
}
