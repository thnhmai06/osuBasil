using Basil.Application.Sessions;
using Basil.Application.UseCases.Scores;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Application.Tests.UseCases.Scores;

public class ScoreStatsCalculatorTests
{
    private static readonly CachedPlayerStats BaseStats = new(
        1_000_000, 500_000, 0, 10, 3600, 300, 1000, 5,
        1, 2, 3, 4, 5);

    private static Beatmap MakeBeatmap(RankedStatus status)
    {
        return new Beatmap(
            "abc", 1, 1, "a", "b", "c", "d",
            DateTime.UtcNow, 1, 999, status, false,
            0, 0, GameMode.VanillaOsu, 1, 1, 1, 1, 1, 1, "f.osu");
    }

    private static ScoreSubmission MakeScore(
        GameMode mode = GameMode.VanillaOsu, long score = 100_000, int n300 = 300, int n100 = 10, int n50 = 5,
        int ngeki = 0, int nkatu = 0, int timeElapsed = 120_000, bool passed = true,
        Grade grade = Grade.S, SubmissionStatus status = SubmissionStatus.Best,
        ScoreSubmission? prevBest = null, Beatmap? bmap = null, int maxCombo = 300)
    {
        return new ScoreSubmission
        {
            Mode = mode, Score = score, N300 = n300, N100 = n100, N50 = n50, NGeki = ngeki, NKatu = nkatu,
            TimeElapsed = timeElapsed, Passed = passed, Grade = grade, Status = status, PrevBest = prevBest,
            Bmap = bmap ?? MakeBeatmap(RankedStatus.Ranked), MaxCombo = maxCombo
        };
    }

    [Fact]
    public void ApplyScoreBaseStats_Osu_ExcludesGekiKatuFromTotalHits()
    {
        var score = MakeScore(n300: 300, n100: 10, n50: 5, ngeki: 999, nkatu: 999,
            timeElapsed: 5000);

        var updated = ScoreStatsCalculator.ApplyScoreBaseStats(score, BaseStats);

        Assert.Equal(BaseStats.TotalHits + 315, updated.TotalHits);
        Assert.Equal(BaseStats.Playtime + 5, updated.Playtime);
        Assert.Equal(BaseStats.Plays + 1, updated.Plays);
        Assert.Equal(BaseStats.Tscore + score.Score, updated.Tscore);
    }

    [Theory]
    [InlineData(GameMode.VanillaTaiko)]
    [InlineData(GameMode.VanillaMania)]
    public void ApplyScoreBaseStats_TaikoOrMania_IncludesGekiKatuInTotalHits(GameMode mode)
    {
        var score = MakeScore(mode, n300: 100, n100: 10, n50: 0, ngeki: 5, nkatu: 3);

        var updated = ScoreStatsCalculator.ApplyScoreBaseStats(score, BaseStats);

        Assert.Equal(BaseStats.TotalHits + 118, updated.TotalHits);
    }

    [Fact]
    public void RankedScoreDelta_NoPrevBest_ReturnsFullScore()
    {
        var score = MakeScore(score: 500_000);

        Assert.Equal(500_000, ScoreStatsCalculator.RankedScoreDelta(score));
    }

    [Fact]
    public void RankedScoreDelta_WithPrevBest_ReturnsDifference()
    {
        var prev = MakeScore(score: 300_000);
        var score = MakeScore(score: 500_000, prevBest: prev);

        Assert.Equal(200_000, ScoreStatsCalculator.RankedScoreDelta(score));
    }

    [Fact]
    public void GradeCountDeltas_FirstScore_BelowA_ReturnsEmpty()
    {
        var score = MakeScore(grade: Grade.B);

        Assert.Empty(ScoreStatsCalculator.GradeCountDeltas(score));
    }

    [Fact]
    public void GradeCountDeltas_FirstScore_AtOrAboveA_ReturnsPlusOne()
    {
        var score = MakeScore(grade: Grade.Xh);

        var deltas = ScoreStatsCalculator.GradeCountDeltas(score);

        Assert.Equal(1, deltas[Grade.Xh]);
        Assert.Single(deltas);
    }

    [Fact]
    public void GradeCountDeltas_SameGradeAsPrevBest_ReturnsEmpty()
    {
        var prev = MakeScore(grade: Grade.S);
        var score = MakeScore(grade: Grade.S, prevBest: prev);

        Assert.Empty(ScoreStatsCalculator.GradeCountDeltas(score));
    }

    [Fact]
    public void GradeCountDeltas_ImprovedFromAToXH_IncrementsNewDecrementsOld()
    {
        var prev = MakeScore(grade: Grade.A);
        var score = MakeScore(grade: Grade.Xh, prevBest: prev);

        var deltas = ScoreStatsCalculator.GradeCountDeltas(score);

        Assert.Equal(1, deltas[Grade.Xh]);
        Assert.Equal(-1, deltas[Grade.A]);
    }

    [Fact]
    public void GradeCountDeltas_DroppedBelowA_OnlyDecrementsOld()
    {
        var prev = MakeScore(grade: Grade.S);
        var score = MakeScore(grade: Grade.C, prevBest: prev);

        var deltas = ScoreStatsCalculator.GradeCountDeltas(score);

        Assert.Equal(-1, deltas[Grade.S]);
        Assert.Single(deltas);
    }

    [Fact]
    public void ApplyRankedScoreStats_UpdatesGradeCountsAndRscore()
    {
        var prev = MakeScore(grade: Grade.A, score: 100_000);
        var score = MakeScore(grade: Grade.S, score: 150_000, prevBest: prev);

        var updated = ScoreStatsCalculator.ApplyRankedScoreStats(score, BaseStats);

        Assert.Equal(BaseStats.SCount + 1, updated.SCount);
        Assert.Equal(BaseStats.ACount - 1, updated.ACount);
        Assert.Equal(BaseStats.Rscore + 50_000, updated.Rscore);
    }

    [Fact]
    public void ApplyScoreStats_Failed_OnlyAppliesBaseStats()
    {
        var score = MakeScore(passed: false, maxCombo: 9999, status: SubmissionStatus.Failed);

        var updated = ScoreStatsCalculator.ApplyScoreStats(score, BaseStats);

        Assert.Equal(BaseStats.MaxCombo, updated.MaxCombo);
        Assert.Equal(BaseStats.Rscore, updated.Rscore);
    }

    [Fact]
    public void ApplyScoreStats_MapWithoutStrictLeaderboard_SkipsMaxComboAndRankedUpdates()
    {
        // Qualified is HasLeaderboard (getscores-style) but NOT HasLeaderboardStrict.
        var score = MakeScore(bmap: MakeBeatmap(RankedStatus.Qualified), maxCombo: 9999, status: SubmissionStatus.Best);

        var updated = ScoreStatsCalculator.ApplyScoreStats(score, BaseStats);

        Assert.Equal(BaseStats.MaxCombo, updated.MaxCombo);
        Assert.Equal(BaseStats.Rscore, updated.Rscore);
    }

    [Fact]
    public void ApplyScoreStats_LovedMap_UpdatesMaxComboButNotRankedScore()
    {
        // Loved is HasLeaderboardStrict but NOT AwardsRankedScore.
        var score = MakeScore(bmap: MakeBeatmap(RankedStatus.Loved), maxCombo: 9999, status: SubmissionStatus.Best);

        var updated = ScoreStatsCalculator.ApplyScoreStats(score, BaseStats);

        Assert.Equal(9999, updated.MaxCombo);
        Assert.Equal(BaseStats.Rscore, updated.Rscore);
    }

    [Fact]
    public void ApplyScoreStats_RankedMapBestScore_UpdatesMaxComboAndRankedScore()
    {
        var prev = MakeScore(grade: Grade.A, score: 100_000);
        var score = MakeScore(
            bmap: MakeBeatmap(RankedStatus.Ranked), maxCombo: 9999, status: SubmissionStatus.Best,
            grade: Grade.S, score: 150_000, prevBest: prev);

        var updated = ScoreStatsCalculator.ApplyScoreStats(score, BaseStats);

        Assert.Equal(9999, updated.MaxCombo);
        Assert.Equal(BaseStats.Rscore + 50_000, updated.Rscore);
        Assert.Equal(BaseStats.SCount + 1, updated.SCount);
    }

    [Fact]
    public void ApplyScoreStats_RankedMapSubmittedNotBest_SkipsRankedUpdatesButKeepsMaxCombo()
    {
        var score = MakeScore(bmap: MakeBeatmap(RankedStatus.Ranked), maxCombo: 9999,
            status: SubmissionStatus.Submitted);

        var updated = ScoreStatsCalculator.ApplyScoreStats(score, BaseStats);

        Assert.Equal(9999, updated.MaxCombo);
        Assert.Equal(BaseStats.Rscore, updated.Rscore);
    }
}