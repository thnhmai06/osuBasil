using Basil.Application.UseCases.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Application.Tests.UseCases.Scores;

public class ScoreSubmissionResponseBuilderTests
{
    [Fact]
    public void BuildSuccess_FailedScore_ReturnsErrorNo()
    {
        var score = new ScoreSubmission { Passed = false };
        var result = new SubmittedScoreResult(score, 1);

        Assert.Equal("error: no", ScoreSubmissionResponseBuilder.BuildSuccess(result, "test.local"));
    }

    [Fact]
    public void BuildSuccess_PassedScore_ReturnsCharts()
    {
        var score = new ScoreSubmission { Passed = true, Bmap = MakeBeatmap(), Id = 1, PlayerId = 1 };
        var result = new SubmittedScoreResult(score, 1);

        var body = ScoreSubmissionResponseBuilder.BuildSuccess(result, "test.local");

        Assert.Contains("beatmapId:", body);
    }

    [Theory]
    [InlineData(ScoreSubmissionResultCode.BeatmapNotFound, "error: beatmap")]
    [InlineData(ScoreSubmissionResultCode.PlayerNotFound, "")]
    [InlineData(ScoreSubmissionResultCode.DuplicateSubmission, "error: no")]
    public void BuildError_MapsEachCode(ScoreSubmissionResultCode code, string expected)
    {
        Assert.Equal(expected, ScoreSubmissionResponseBuilder.BuildError(code));
    }

    private static Beatmap MakeBeatmap()
    {
        return new Beatmap(
            "md5", 1, 1, "a", "b", "c", "d", DateTime.UtcNow, 1, 500, RankedStatus.Ranked, false, 0, 0,
            GameMode.VanillaOsu, 1, 1, 1, 1, 1, 1, "f.osu");
    }
}