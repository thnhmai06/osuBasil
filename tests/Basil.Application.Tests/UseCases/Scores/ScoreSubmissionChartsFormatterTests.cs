using Basil.Application.Services.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Application.Tests.UseCases.Scores;

public class ScoreSubmissionChartsFormatterTests
{
    private static Beatmap MakeBeatmap()
    {
        var mapset = new Mapset(50, "a", "b", "d",
            new DateTime(2021, 5, 20, 10, 0, 0, DateTimeKind.Utc), new DateTime(2021, 5, 20, 10, 0, 0, DateTimeKind.Utc));
        return new Beatmap(
            "md5", 100, mapset, "c", "f.osu", TimeSpan.FromSeconds(1), 500, 10, 5,
            new Difficulty(GameMode.Standard, 1, 1, 1, 1, 1, 1));
    }

    private static ScoreSubmission MakeScore(Beatmap beatmap, long score, int maxCombo, HitCounts hitCounts)
    {
        return new ScoreSubmission
        {
            BeatmapMd5 = beatmap.Md5,
            UserId = 7,
            Score = score,
            MaxCombo = maxCombo,
            HitCounts = hitCounts,
            Mode = GameMode.Standard,
            Mods = Mods.NoMod,
            Grade = Grade.S,
            IsPassed = true,
            IsFullCombo = false,
            ClientTime = DateTime.UtcNow
        };
    }

    [Fact]
    public void Format_FirstScoreOnMap_BeforeValuesAreEmpty()
    {
        var beatmap = MakeBeatmap();
        var score = MakeScore(beatmap, 500_000, 500, new HitCounts(300, 0, 0, 0, 0, 0)); // 100% accuracy

        var result = ScoreSubmissionChartsFormatter.Format(score, beatmap, 42, 3, "test.local");

        Assert.Contains(
            "beatmapId:100|beatmapSetId:50|beatmapPlaycount:10|beatmapPasscount:5|approvedDate:2021-05-20 10:00:00",
            result);
        Assert.Contains("chartId:beatmap|chartUrl:https://osu.test.local/s/50|chartName:Beatmap Ranking", result);
        Assert.Contains("rankBefore:|rankAfter:3", result);
        Assert.Contains("rankedScoreBefore:|rankedScoreAfter:500000", result);
        Assert.Contains("accuracyBefore:|accuracyAfter:100", result);
        Assert.Contains("ppBefore:|ppAfter:", result);
        Assert.Contains("onlineScoreId:42", result);
        Assert.Contains("chartId:overall|chartUrl:https://test.local/u/7|chartName:Overall Ranking", result);
        Assert.EndsWith("achievements-new:", result);
    }

    [Fact]
    public void Format_OverallSection_HasNoStatsDeltaSinceStatsAreFixed()
    {
        var beatmap = MakeBeatmap();
        var score = MakeScore(beatmap, 1, 1, new HitCounts(1, 0, 0, 0, 0, 0));

        var result = ScoreSubmissionChartsFormatter.Format(score, beatmap, 1, 1, "test.local");

        Assert.Contains("chartId:overall", result);
        var overallSection = result[result.IndexOf("chartId:overall", StringComparison.Ordinal)..];
        Assert.Contains("rankedScoreBefore:|rankedScoreAfter:", overallSection);
        Assert.Contains("totalScoreBefore:|totalScoreAfter:", overallSection);
    }
}
