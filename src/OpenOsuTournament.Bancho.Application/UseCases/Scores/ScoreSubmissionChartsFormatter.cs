using System.Globalization;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Domain.Scores;

namespace OpenOsuTournament.Bancho.Application.UseCases.Scores;

/// <summary>
///     Ported from app/api/domains/osu.py's chart_entry/build_submission_charts. Achievements are
///     dropped entirely (per scope) — the achievements-new field is always empty. `pp` chart entries
///     are always passed 0 (no-pp scope); chart_entry's own "0 or ''" falsy formatting empties them
///     automatically, so the field is kept (preserving the protocol's key/value shape for the osu!
///     client's result-screen parser) without any pp-specific special-casing.
/// </summary>
public static class ScoreSubmissionChartsFormatter
{
    public static string Format(ScoreSubmission score, CachedPlayerStats previousStats, CachedPlayerStats currentStats,
        string domain)
    {
        var beatmapEntries = score.PrevBest is { } prevBest
            ? new[]
            {
                ChartEntry("rank", prevBest.Rank, score.Rank),
                ChartEntry("rankedScore", prevBest.Score, score.Score),
                ChartEntry("totalScore", prevBest.Score, score.Score),
                ChartEntry("maxCombo", prevBest.MaxCombo, score.MaxCombo),
                ChartEntry("accuracy", Math.Round(prevBest.Acc, 2), Math.Round(score.Acc, 2)),
                ChartEntry("pp", 0, 0)
            }
            : new[]
            {
                ChartEntry("rank", null, score.Rank),
                ChartEntry("rankedScore", null, score.Score),
                ChartEntry("totalScore", null, score.Score),
                ChartEntry("maxCombo", null, score.MaxCombo),
                ChartEntry("accuracy", null, Math.Round(score.Acc, 2)),
                ChartEntry("pp", null, 0)
            };

        var overallEntries = new[]
        {
            ChartEntry("rank", previousStats.Rank, currentStats.Rank),
            ChartEntry("rankedScore", previousStats.Rscore, currentStats.Rscore),
            ChartEntry("totalScore", previousStats.Tscore, currentStats.Tscore),
            ChartEntry("maxCombo", previousStats.MaxCombo, currentStats.MaxCombo),
            ChartEntry("accuracy", Math.Round(previousStats.Acc, 2), Math.Round(currentStats.Acc, 2)),
            ChartEntry("pp", 0, 0)
        };

        var bmap = score.Bmap!;
        var parts = new List<string>
        {
            $"beatmapId:{bmap.Id}",
            $"beatmapSetId:{bmap.SetId}",
            $"beatmapPlaycount:{bmap.Plays}",
            $"beatmapPasscount:{bmap.Passes}",
            $"approvedDate:{bmap.LastUpdate:yyyy-MM-dd HH:mm:ss}",
            "\n",
            "chartId:beatmap",
            $"chartUrl:https://osu.{domain}/s/{bmap.SetId}",
            "chartName:Beatmap Ranking"
        };
        parts.AddRange(beatmapEntries);
        parts.Add($"onlineScoreId:{score.Id}");
        parts.Add("\n");
        parts.Add("chartId:overall");
        parts.Add($"chartUrl:https://{domain}/u/{score.PlayerId}");
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
            int i when i == 0 => "",
            long l when l == 0 => "",
            double d when d == 0.0 => "",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }
}