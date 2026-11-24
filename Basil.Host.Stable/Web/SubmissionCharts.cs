using Basil.Application.Scores.Persistence;
using Basil.Domain.Beatmaps;
using Basil.Protocol.Game.Stable.Models.Web;

namespace Basil.Host.Stable.Web;

internal static class SubmissionCharts
{
	public static SubmissionReply Build(ScoreReceipt receipt, BeatmapRecord? beatmap)
	{
		var score = receipt.Score.Value;
		var previous = receipt.PreviousBest?.Value;
		var none = new ChartEntry("rank", null, null);
		return new SubmissionReply(beatmap?.Id ?? 0, beatmap?.Beatmapset.Id ?? 0, beatmap?.Beatmapset.UpdatedAt,
			new SubmissionChart(OsuWebReplies.BeatmapChartId, OsuWebReplies.BeatmapChartName,
			[
				none,
				new ChartEntry("rankedScore", previous?.LastState.TotalScore, score.LastState.TotalScore),
				new ChartEntry("totalScore", previous?.LastState.TotalScore, score.LastState.TotalScore),
				new ChartEntry("maxCombo", previous?.LastState.MaxCombo, score.LastState.MaxCombo),
				new ChartEntry("accuracy", previous?.Accuracy, score.Accuracy, ChartValueKind.Fixed),
				new ChartEntry("pp", previous?.PerformancePoints, score.PerformancePoints, ChartValueKind.Fixed)
			], receipt.Score.Id),
			new SubmissionChart(OsuWebReplies.OverallChartId, OsuWebReplies.OverallChartName,
			[
				none,
				new ChartEntry("rankedScore", receipt.RankedScoreBefore, receipt.Stats.RankedScore),
				new ChartEntry("totalScore", receipt.TotalScoreBefore, receipt.Stats.TotalScore),
				new ChartEntry("maxCombo", null, null),
				new ChartEntry("accuracy", null, null, ChartValueKind.Fixed),
				new ChartEntry("pp", null, null, ChartValueKind.Fixed)
			], null));
	}
}
