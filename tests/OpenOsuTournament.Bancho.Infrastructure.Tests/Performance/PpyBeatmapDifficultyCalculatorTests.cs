using OpenOsuTournament.Bancho.Domain;
using OpenOsuTournament.Bancho.Domain.Beatmaps;
using OpenOsuTournament.Bancho.Infrastructure.Performance;

namespace OpenOsuTournament.Bancho.Infrastructure.Tests.Performance;

/// <summary>
///     Verifies the ppy.osu.Game-backed difficulty engine produces stable star ratings for
///     Fixtures/vivid_osu_file.osu across representative mod combinations. Reference values were
///     recorded by running this calculator directly — not a cross-check against any other engine's
///     output (the old akatsuki-pp-rs reference values no longer apply).
/// </summary>
public class PpyBeatmapDifficultyCalculatorTests
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
        var calculator = new PpyBeatmapDifficultyCalculator();

        var stars = calculator.CalculateStarRating(FixturePath, GameMode.VanillaOsu, mods);

        Assert.Equal(expectedStars, stars, 10);
    }

    [Fact]
    public void CalculateStarRating_NonexistentFile_Throws()
    {
        var calculator = new PpyBeatmapDifficultyCalculator();

        Assert.Throws<InvalidOperationException>(() =>
            calculator.CalculateStarRating(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "does-not-exist.osu"),
                GameMode.VanillaOsu,
                Mods.NoMod));
    }
}
