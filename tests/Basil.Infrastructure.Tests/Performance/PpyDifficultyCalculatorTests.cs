using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Infrastructure.Performance;

namespace Basil.Infrastructure.Tests.Performance;

/// <summary>
///     Verifies the ppy.osu.Game-backed difficulty engine produces stable star ratings for
///     Fixtures/vivid_osu_file.osu across representative mod combinations. Reference values were
///     recorded by running this calculator directly — not a cross-check against any other engine's
///     output (the old akatsuki-pp-rs reference values no longer apply).
/// </summary>
public class PpyDifficultyCalculatorTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu");

    [Theory]
    [InlineData(Mods.NoMod, 4.8750450142072701)]
    [InlineData(Mods.HardRock, 5.9296060838721534)]
    [InlineData(Mods.DoubleTime, 7.0477415498633968)]
    [InlineData(Mods.Hidden | Mods.DoubleTime, 7.2215498802893343)]
    public void CalculateStarRating_MatchesRecordedReference(Mods mods, double expectedStars)
    {
        var calculator = new PpyDifficultyCalculator();

        var stars = calculator.CalculateStarRating(FixturePath, GameMode.Standard, mods);

        Assert.Equal(expectedStars, stars, 10);
    }

    [Fact]
    public void CalculateStarRating_NonexistentFile_Throws()
    {
        var calculator = new PpyDifficultyCalculator();

        Assert.Throws<InvalidOperationException>(() =>
            calculator.CalculateStarRating(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "does-not-exist.osu"),
                GameMode.Standard,
                Mods.NoMod));
    }
}