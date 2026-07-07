using Bancho.Infrastructure.Performance;

namespace Bancho.Infrastructure.Tests.Performance;

/// <summary>
///     Verifies the P/Invoke binding into the native akatsuki-pp-rs difficulty engine (see
///     native/bancho-pp-ffi) produces star ratings that are bit-for-bit identical to
///     akatsuki-pp-py==1.0.5 (the dependency bancho.py itself uses), for the same beatmap +
///     mod combinations. Reference values were generated directly from akatsuki-pp-py against
///     testing/sample_data/vivid_osu_file.osu in the bancho.py repo.
/// </summary>
public class NativeBeatmapDifficultyCalculatorTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu");

    [Theory]
    [InlineData(0, 5.23375459182082)] // NoMod
    [InlineData(16, 6.072634190828741)] // HR
    [InlineData(64, 7.370721327151392)] // DT
    [InlineData(72, 7.370721327151392)] // HD+DT
    public void CalculateStarRating_MatchesAkatsukiPpPyReference(int mods, double expectedStars)
    {
        var calculator = new NativeBeatmapDifficultyCalculator();

        var stars = calculator.CalculateStarRating(FixturePath, mods);

        Assert.Equal(expectedStars, stars, 10);
    }

    [Fact]
    public void CalculateStarRating_NonexistentFile_Throws()
    {
        var calculator = new NativeBeatmapDifficultyCalculator();

        Assert.Throws<InvalidOperationException>(() =>
            calculator.CalculateStarRating(Path.Combine(AppContext.BaseDirectory, "Fixtures", "does-not-exist.osu"),
                0));
    }
}