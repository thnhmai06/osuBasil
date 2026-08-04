using System.Globalization;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Application.Services.Scores;

/// <summary>
///     Formats the chart section of the score submission response.
/// </summary>
/// <remarks>
///     The output is a pipe-delimited list of key/value pairs describing a beatmap ranking chart
///     and an overall ranking chart, which the osu! client's result screen parses. Achievements
///     are out of scope, so the achievements field is always empty, and plays and passes are not
///     tracked, so the play and pass counts are always emitted as zero. PP chart entries are
///     always passed zero because Basil has no pp. These zero and empty values keep the protocol's
///     fixed key/value shape intact without any pp-specific or plays/passes-specific
///     special-casing. The overall chart carries no before/after delta because user stats are
///     never updated on submission, so every entry is emitted empty rather than reading stats that
///     would never change.
/// </remarks>
public static class ScoreSubmissionChartsFormatter
{
	/// <summary>
	///     Formats the complete chart section for a single score submission.
	/// </summary>
	/// <param name="score">The submitted score being charted.</param>
	/// <param name="beatmap">The beatmap the score was played on.</param>
	/// <param name="scoreId">The database id assigned to the submitted score.</param>
	/// <param name="rank">The per-beatmap rank reported to the client, or <see langword="null" /> for a failed play.</param>
	/// <param name="domain">The server's domain, used to build the chart URLs.</param>
	/// <returns>The pipe-delimited chart string for the submission.</returns>
	/// <remarks>
	///     The emitted sequence is the beatmap header (ids, play and pass counts, approval date), the
	///     <c>beatmap</c> chart with its ranking entries, the online score id, and the
	///     <c>overall</c> chart with its always-empty entries, terminated by an empty achievements
	///     field.
	/// </remarks>
	public static string Format(Submission score, Beatmap beatmap, long scoreId, int? rank, string domain)
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
			$"beatmapSetId:{beatmap.Beatmapset.Id}",
			"beatmapPlaycount:0",
			"beatmapPasscount:0",
			$"approvedDate:{beatmap.Beatmapset.LastUpdate:yyyy-MM-dd HH:mm:ss}",
			"\n",
			"chartId:beatmap",
			$"chartUrl:https://osu.{domain}/s/{beatmap.Beatmapset.Id}",
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
			0 => "",
			long and 0 => "",
			0.0 => "",
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString() ?? ""
		};
	}
}