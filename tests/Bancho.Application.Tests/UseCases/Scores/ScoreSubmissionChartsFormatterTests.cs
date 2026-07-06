using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Scores;
using Bancho.Domain;

namespace Bancho.Application.Tests.UseCases.Scores;

public class ScoreSubmissionChartsFormatterTests
{
    private static Beatmap MakeBeatmap() => new(
        Md5: "md5", Id: 100, SetId: 50, Artist: "a", Title: "b", Version: "c", Creator: "d",
        LastUpdate: new DateTime(2021, 5, 20, 10, 0, 0, DateTimeKind.Utc), TotalLength: 1, MaxCombo: 500,
        Status: RankedStatus.Ranked, Frozen: false, Plays: 10, Passes: 5, Mode: GameMode.VanillaOsu,
        Bpm: 1, Cs: 1, Od: 1, Ar: 1, Hp: 1, Diff: 1, Filename: "f.osu");

    [Fact]
    public void Format_FirstScoreOnMap_BeforeValuesAreEmpty()
    {
        var score = new ScoreSubmission
        {
            Id = 42, Bmap = MakeBeatmap(), PlayerId = 7, Score = 500_000, MaxCombo = 500, Acc = 98.1234, Rank = 3,
        };
        var previousStats = new CachedPlayerStats(0, 0, 0, 0, 0, 0, 0, 0);
        var currentStats = new CachedPlayerStats(500_000, 500_000, 0, 1, 60, 500, 315, 3);

        var result = ScoreSubmissionChartsFormatter.Format(score, previousStats, currentStats, "test.local");

        Assert.Contains("beatmapId:100|beatmapSetId:50|beatmapPlaycount:10|beatmapPasscount:5|approvedDate:2021-05-20 10:00:00", result);
        Assert.Contains("chartId:beatmap|chartUrl:https://osu.test.local/s/50|chartName:Beatmap Ranking", result);
        Assert.Contains("rankBefore:|rankAfter:3", result);
        Assert.Contains("rankedScoreBefore:|rankedScoreAfter:500000", result);
        Assert.Contains("accuracyBefore:|accuracyAfter:98.12", result);
        Assert.Contains("ppBefore:|ppAfter:", result);
        Assert.Contains("onlineScoreId:42", result);
        Assert.Contains("chartId:overall|chartUrl:https://test.local/u/7|chartName:Overall Ranking", result);
        Assert.Contains("rankedScoreBefore:|rankedScoreAfter:500000", result);
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
            PrevBest = new ScoreSubmission { Score = 500_000, MaxCombo = 400, Acc = 95.0, Rank = 2 },
        };
        var previousStats = new CachedPlayerStats(500_000, 500_000, 95.0, 1, 60, 400, 300, 2);
        var currentStats = new CachedPlayerStats(1_100_000, 600_000, 99.0, 2, 120, 500, 615, 1);

        var result = ScoreSubmissionChartsFormatter.Format(score, previousStats, currentStats, "test.local");

        Assert.Contains("rankBefore:2|rankAfter:1", result);
        Assert.Contains("rankedScoreBefore:500000|rankedScoreAfter:600000", result);
        Assert.Contains("maxComboBefore:400|maxComboAfter:500", result);
        Assert.Contains("accuracyBefore:95|accuracyAfter:99", result);
    }
}
