using Basil.Application.Scores.Persistence;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Host.Stable.Web;
using Basil.Testing.Persistence;

namespace Basil.Host.Stable.Tests.Web;

public sealed class SubmissionChartsTests
{
	private const string Origin = "https://osu.example";

	private static ScoreRecord Score(int id, int total, int combo, HitCounts hits, double? pp = null)
	{
		return new ScoreRecord
		{
			Id = id,
			Value = new ScoreData(5, StoredBeatmaps.Beatmap().Hash, GameMode.Standard, GameMods.NoMod,
				new ScoreState(hits, total, (ushort)combo, (ushort)combo, false), Grade.A, true,
				DateTimeOffset.UnixEpoch) { Pp = pp }
		};
	}

	[Fact]
	public void BeatmapBeaten_ChartsCompareWithPreviousBest()
	{
		var previous = Score(40, 1000, 90, new HitCounts(90, 10, 0, 0, 0, 0), 120.456);
		var now = Score(42, 1500, 100, new HitCounts(100, 0, 0, 0, 0, 0));
		var stats = new UserStats { UserId = 5, Mode = GameMode.Standard, TotalScore = 2500, RankedScore = 2500 };

		var text = SubmissionCharts.Build(new ScoreReceipt(now, stats, previous), StoredBeatmaps.Beatmap(), Origin);

		Assert.Equal(
			"beatmapId:321|beatmapSetId:100|beatmapPlaycount:0|beatmapPasscount:0|approvedDate:1970-01-01 00:00:00|\n" +
			"|chartId:beatmap|chartUrl:https://osu.example/beatmapsets/100|chartName:Beatmap Ranking" +
			"|rankBefore:|rankAfter:|rankedScoreBefore:1000|rankedScoreAfter:1500" +
			"|totalScoreBefore:1000|totalScoreAfter:1500|maxComboBefore:90|maxComboAfter:100" +
			"|accuracyBefore:93.33|accuracyAfter:100.0|ppBefore:120.46|ppAfter:|onlineScoreId:42|\n" +
			"|chartId:overall|chartUrl:https://osu.example/u/5|chartName:Overall Ranking" +
			"|rankBefore:|rankAfter:|rankedScoreBefore:1000|rankedScoreAfter:2500" +
			"|totalScoreBefore:1000|totalScoreAfter:2500|maxComboBefore:|maxComboAfter:" +
			"|accuracyBefore:|accuracyAfter:|ppBefore:|ppAfter:|achievements-new:",
			text);
	}

	[Fact]
	public void FirstPlay_EveryBeforeValueEmpty()
	{
		var now = Score(1, 800, 50, new HitCounts(50, 0, 0, 0, 0, 0));
		var stats = new UserStats { UserId = 5, Mode = GameMode.Standard, TotalScore = 800, RankedScore = 800 };

		var text = SubmissionCharts.Build(new ScoreReceipt(now, stats, null), StoredBeatmaps.Beatmap(), Origin);

		Assert.Contains("|rankedScoreBefore:|rankedScoreAfter:800|totalScoreBefore:|totalScoreAfter:800|" +
		                "maxComboBefore:|maxComboAfter:50|accuracyBefore:|accuracyAfter:100.0|ppBefore:|ppAfter:|" +
		                "onlineScoreId:1|", text);
		Assert.EndsWith("|rankedScoreBefore:|rankedScoreAfter:800|totalScoreBefore:|totalScoreAfter:800|" +
		                "maxComboBefore:|maxComboAfter:|accuracyBefore:|accuracyAfter:|ppBefore:|ppAfter:|" +
		                "achievements-new:", text);
	}

	[Fact]
	public void UnknownBeatmap_MetadataLineHasNoBeatmap()
	{
		var now = Score(1, 800, 50, new HitCounts(50, 0, 0, 0, 0, 0));
		var stats = new UserStats { UserId = 5, Mode = GameMode.Standard, TotalScore = 800, RankedScore = 800 };

		var text = SubmissionCharts.Build(new ScoreReceipt(now, stats, null), null, Origin);

		Assert.StartsWith("beatmapId:0|beatmapSetId:0|beatmapPlaycount:0|beatmapPasscount:0|approvedDate:|\n|chartId:beatmap|",
			text);
	}

	[Fact]
	public void PpAfter_AlwaysEmpty_EvenWhenScoreHasPp()
	{
		var now = Score(1, 800, 50, new HitCounts(50, 0, 0, 0, 0, 0), 99.9);
		var stats = new UserStats { UserId = 5, Mode = GameMode.Standard, TotalScore = 800, RankedScore = 800 };

		var text = SubmissionCharts.Build(new ScoreReceipt(now, stats, null), StoredBeatmaps.Beatmap(), Origin);

		Assert.DoesNotContain("99.9", text);
	}
}
