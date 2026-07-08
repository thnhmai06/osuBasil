using Basil.Domain.Scores;

namespace Basil.Domain.Tests;

public class ScoreAccuracyCalculatorTests
{
    [Fact]
    public void Osu_AllTypesCounted()
    {
        var acc = ScoreAccuracyCalculator.Calculate(0, 90, 5, 3, 0, 0, 2, Mods.NoMod);

        var expected = 100.0 * (90 * 300.0 + 5 * 100.0 + 3 * 50.0) / (100 * 300.0);
        Assert.Equal(expected, acc, 10);
    }

    [Fact]
    public void Osu_NoObjectsHit_ReturnsZero()
    {
        Assert.Equal(0.0, ScoreAccuracyCalculator.Calculate(0, 0, 0, 0, 0, 0, 0, Mods.NoMod));
    }

    [Fact]
    public void Taiko_UsesHalfWeightFor100s()
    {
        var acc = ScoreAccuracyCalculator.Calculate(1, 80, 10, 0, 0, 0, 10, Mods.NoMod);

        var expected = 100.0 * (10 * 0.5 + 80) / 100;
        Assert.Equal(expected, acc, 10);
    }

    [Fact]
    public void Catch_CountsFruitsAndDropletsOnly()
    {
        var acc = ScoreAccuracyCalculator.Calculate(2, 90, 5, 3, 0, 1, 1, Mods.NoMod);

        var expected = 100.0 * (90 + 5 + 3) / 100;
        Assert.Equal(expected, acc, 10);
    }

    [Fact]
    public void Mania_WithoutScoreV2_TreatsGekiAsPerfect300()
    {
        var acc = ScoreAccuracyCalculator.Calculate(3, 50, 20, 10, 15, 5, 0, Mods.NoMod);

        var total = 50 + 20 + 10 + 15 + 5;
        var expected = 100.0 * (10 * 50.0 + 20 * 100.0 + 5 * 200.0 + (50 + 15) * 300.0) / (total * 300.0);
        Assert.Equal(expected, acc, 10);
    }

    [Fact]
    public void Mania_WithScoreV2_WeighsGekiHigherThanPerfect300()
    {
        var acc = ScoreAccuracyCalculator.Calculate(3, 50, 20, 10, 15, 5, 0, Mods.ScoreV2);

        var total = 50 + 20 + 10 + 15 + 5;
        var expected = 100.0 * (10 * 50.0 + 20 * 100.0 + 5 * 200.0 + 50 * 300.0 + 15 * 305.0) / (total * 305.0);
        Assert.Equal(expected, acc, 10);
    }

    [Fact]
    public void InvalidMode_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScoreAccuracyCalculator.Calculate(4, 0, 0, 0, 0, 0, 0, Mods.NoMod));
    }
}