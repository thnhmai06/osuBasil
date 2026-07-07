using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Scores;
using Bancho.Domain;
using Bancho.Domain.Beatmaps;
using Bancho.Domain.Scores;

namespace Bancho.Application.Tests.UseCases.Scores;

public class ScoreSubmissionResponseBuilderTests
{
    [Fact]
    public void BuildSuccess_FailedScore_ReturnsErrorNo()
    {
        var score = new ScoreSubmission { Passed = false };
        var stats = new CachedPlayerStats(0, 0, 0, 0, 0, 0, 0, 0);
        var result = new SubmittedScoreResult(score, 1, stats, stats);

        Assert.Equal("error: no", ScoreSubmissionResponseBuilder.BuildSuccess(result, "test.local"));
    }

    [Fact]
    public void BuildSuccess_PassedScore_ReturnsCharts()
    {
        var score = new ScoreSubmission { Passed = true, Bmap = MakeBeatmap(), Id = 1, PlayerId = 1 };
        var stats = new CachedPlayerStats(0, 0, 0, 0, 0, 0, 0, 0);
        var result = new SubmittedScoreResult(score, 1, stats, stats);

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

    private static Beatmap MakeBeatmap() => new(
        "md5", 1, 1, "a", "b", "c", "d", DateTime.UtcNow, 1, 500, RankedStatus.Ranked, false, 0, 0,
        GameMode.VanillaOsu, 1, 1, 1, 1, 1, 1, "f.osu");
}
