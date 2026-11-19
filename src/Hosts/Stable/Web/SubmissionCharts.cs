using System.Globalization;
using Basil.Application.Scores.Persistence;
using Basil.Domain.Beatmaps;

namespace Basil.Host.Stable.Web;

internal static class SubmissionCharts
{
	private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

	public static string Build(ScoreReceipt receipt, BeatmapRecord? beatmap, string origin)
	{
		var score = receipt.Score.Value;
		var previous = receipt.PreviousBest?.Value;
		var setId = beatmap?.Beatmapset.Id ?? 0;
		List<string> parts =
		[
			$"beatmapId:{beatmap?.Id ?? 0}",
			$"beatmapSetId:{setId}",
			"beatmapPlaycount:0",
			"beatmapPasscount:0",
			$"approvedDate:{beatmap?.Beatmapset.UpdatedAt.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture)}",
			"\n",
			$"chartId:{OsuWebReplies.BeatmapChartId}",
			$"chartUrl:{origin}/beatmapsets/{setId}",
			$"chartName:{OsuWebReplies.BeatmapChartName}",
			Entry("rank", "", ""),
			Entry("rankedScore", Whole(previous?.LastState.TotalScore), Whole(score.LastState.TotalScore)),
			Entry("totalScore", Whole(previous?.LastState.TotalScore), Whole(score.LastState.TotalScore)),
			Entry("maxCombo", Whole(previous?.LastState.MaxCombo), Whole(score.LastState.MaxCombo)),
			Entry("accuracy", Fixed(previous?.Accuracy), Fixed(score.Accuracy)),
			Entry("pp", Fixed(previous?.PerformancePoints), ""),
			$"onlineScoreId:{receipt.Score.Id}",
			"\n",
			$"chartId:{OsuWebReplies.OverallChartId}",
			$"chartUrl:{origin}/u/{score.UserId}",
			$"chartName:{OsuWebReplies.OverallChartName}",
			Entry("rank", "", ""),
			Entry("rankedScore", Whole(receipt.RankedScoreBefore), Whole(receipt.Stats.RankedScore)),
			Entry("totalScore", Whole(receipt.TotalScoreBefore), Whole(receipt.Stats.TotalScore)),
			Entry("maxCombo", "", ""),
			Entry("accuracy", "", ""),
			Entry("pp", "", ""),
			OsuWebReplies.NewAchievements
		];
		return string.Join('|', parts);
	}

	private static string Entry(string name, string before, string after)
	{
		return $"{name}Before:{before}|{name}After:{after}";
	}

	private static string Whole(long? value)
	{
		return value is null or 0 ? string.Empty : value.Value.ToString(CultureInfo.InvariantCulture);
	}

	private static string Fixed(double? value)
	{
		return value is { } number && Math.Round(number, 2) is var rounded and not 0
			? rounded.ToString("0.0#", CultureInfo.InvariantCulture)
			: string.Empty;
	}
}