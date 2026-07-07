using OpenOsuTournament.Bancho.Application.UseCases.Scores;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Domain.Scores;

namespace OpenOsuTournament.Bancho.Application.Tests.UseCases.Scores;

public class ScoreSubmissionChartsFormatterTests
{
    private static Beatmap MakeBeatmap()
    {
        return new Beatmap(
            "md5", 100, 50, "a", "b", "c", "d",
            new DateTime(2021, 5, 20, 10, 0, 0, DateTimeKind.Utc), 1, 500,
            RankedStatus.Ranked, false, 10, 5, GameMode.VanillaOsu,
            1, 1, 1, 1, 1, 1, "f.osu");
    }

    [Fact]
    public void Format_FirstScoreOnMap_BeforeValuesAreEmpty()
    {
        var score = new ScoreSubmission
        {
            Id = 42, Bmap = MakeBeatmap(), PlayerId = 7, Score = 500_000, MaxCombo = 500, Acc = 98.1234, Rank = 3
        };

        var result = ScoreSubmissionChartsFormatter.Format(score, "test.local");

        Assert.Contains(
            "beatmapId:100|beatmapSetId:50|beatmapPlaycount:10|beatmapPasscount:5|approvedDate:2021-05-20 10:00:00",
            result);
        Assert.Contains("chartId:beatmap|chartUrl:https://osu.test.local/s/50|chartName:Beatmap Ranking", result);
        Assert.Contains("rankBefore:|rankAfter:3", result);
        Assert.Contains("rankedScoreBefore:|rankedScoreAfter:500000", result);
        Assert.Contains("accuracyBefore:|accuracyAfter:98.12", result);
        Assert.Contains("ppBefore:|ppAfter:", result);
        Assert.Contains("onlineScoreId:42", result);
        Assert.Contains("chartId:overall|chartUrl:https://test.local/u/7|chartName:Overall Ranking", result);
        Assert.EndsWith("achievements-new:", result);
    }

    [Fact]
    public void Format_ImprovedOverPreviousBest_ShowsBeforeAndAfterValues()
    {
        var score = new ScoreSubmission
        {
            Id = 43,
            Bmap = MakeBeatmap(),
            PlayerId = 7,
            Score = 600_000,
            MaxCombo = 500,
            Acc = 99.0,
            Rank = 1,
            PrevBest = new ScoreSubmission { Score = 500_000, MaxCombo = 400, Acc = 95.0, Rank = 2 }
        };

        var result = ScoreSubmissionChartsFormatter.Format(score, "test.local");

        Assert.Contains("rankBefore:2|rankAfter:1", result);
        Assert.Contains("rankedScoreBefore:500000|rankedScoreAfter:600000", result);
        Assert.Contains("maxComboBefore:400|maxComboAfter:500", result);
        Assert.Contains("accuracyBefore:95|accuracyAfter:99", result);
    }

    [Fact]
    public void Format_OverallSection_HasNoStatsDeltaSinceStatsAreFixed()
    {
        var score = new ScoreSubmission { Id = 1, Bmap = MakeBeatmap(), PlayerId = 7, Score = 1, Rank = 1 };

        var result = ScoreSubmissionChartsFormatter.Format(score, "test.local");

        Assert.Contains("chartId:overall", result);
        var overallSection = result[result.IndexOf("chartId:overall", StringComparison.Ordinal)..];
        Assert.Contains("rankedScoreBefore:|rankedScoreAfter:", overallSection);
        Assert.Contains("totalScoreBefore:|totalScoreAfter:", overallSection);
    }
}
