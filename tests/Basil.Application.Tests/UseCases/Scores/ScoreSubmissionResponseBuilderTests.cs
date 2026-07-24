using Basil.Application.Services.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Application.Tests.UseCases.Scores;

public class ScoreSubmissionResponseBuilderTests
{
    [Fact]
    public void BuildSuccess_FailedScore_ReturnsErrorNo()
    {
        var beatmap = MakeBeatmap();
        var score = MakeScore(beatmap, passed: false);
        var result = new SubmittedScoreResult(score, 1, beatmap, "cmyui", null);

        Assert.Equal("error: no", ScoreSubmissionResponseBuilder.BuildSuccess(result, "test.local"));
    }

    [Fact]
    public void BuildSuccess_PassedScore_ReturnsCharts()
    {
        var beatmap = MakeBeatmap();
        var score = MakeScore(beatmap, passed: true);
        var result = new SubmittedScoreResult(score, 1, beatmap, "cmyui", null);

        var body = ScoreSubmissionResponseBuilder.BuildSuccess(result, "test.local");

        Assert.Contains("beatmapId:", body);
    }

    [Theory]
    [InlineData(ScoreSubmissionResultCode.BeatmapNotFound, "error: beatmap")]
    [InlineData(ScoreSubmissionResultCode.PlayerNotFound, "")]
    [InlineData(ScoreSubmissionResultCode.DuplicateSubmission, "error: no")]
    [InlineData(ScoreSubmissionResultCode.NotInMultiplayer, "error: no")]
    public void BuildError_MapsEachCode(ScoreSubmissionResultCode code, string expected)
    {
        Assert.Equal(expected, ScoreSubmissionResponseBuilder.BuildError(code));
    }

    private static Beatmap MakeBeatmap()
    {
        var mapset = new Mapset(1, "a", "b", "d", DateTime.UtcNow, DateTime.UtcNow);
        return new Beatmap(
            "md5", 1, mapset, "c", "f.osu", TimeSpan.FromSeconds(1), 500, 0, 0,
            new Difficulty(GameMode.Standard, 1, 1, 1, 1, 1, 1), new Dictionary<string, int>());
    }

    private static ScoreSubmission MakeScore(Beatmap beatmap, bool passed)
    {
        return new ScoreSubmission
        {
            BeatmapMd5 = beatmap.Md5,
            UserId = 1,
            Mode = GameMode.Standard,
            Mods = Mods.NoMod,
            HitCounts = new HitCounts(300, 0, 0, 0, 0, 0),
            Score = 500_000,
            MaxCombo = 500,
            Grade = Grade.S,
            IsPassed = passed,
            IsFullCombo = false,
            ClientTime = DateTime.UtcNow
        };
    }
}
