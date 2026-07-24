using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Infrastructure.Performance;

namespace Basil.Infrastructure.Tests.Performance;

/// <summary>
///     Verifies the ppy.osu.Game-backed calculation engine produces stable star ratings and
///     hit-object counts for Fixtures/vivid_osu_file.osu across representative mod combinations.
///     Reference values were recorded by running this calculator directly — not a cross-check
///     against any other engine's output (the old akatsuki-pp-rs reference values no longer apply).
/// </summary>
public class PpyOsuCalculatorTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu");

    [Theory]
    [InlineData(Mods.NoMod, 4.8750450142072701)]
    [InlineData(Mods.HardRock, 5.9296060838721534)]
    [InlineData(Mods.DoubleTime, 7.0477415498633968)]
    [InlineData(Mods.Hidden | Mods.DoubleTime, 7.2215498802893343)]
    public void Analyze_StarRating_MatchesRecordedReference(Mods mods, double expectedStars)
    {
        var calculator = new PpyOsuCalculator();

        var analysis = calculator.Analyze(FixturePath, GameMode.Standard, mods);

        Assert.Equal(expectedStars, analysis.StarRating, 10);
    }

    [Fact]
    public void Analyze_NonexistentFile_Throws()
    {
        var calculator = new PpyOsuCalculator();

        Assert.Throws<InvalidOperationException>(() =>
            calculator.Analyze(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "does-not-exist.osu"),
                GameMode.Standard,
                Mods.NoMod));
    }

    [Fact]
    public void Analyze_StandardFixture_ReturnsNonEmptyObjectCounts()
    {
        var calculator = new PpyOsuCalculator();

        var analysis = calculator.Analyze(FixturePath, GameMode.Standard, Mods.NoMod);

        Assert.NotEmpty(analysis.ObjectCounts);
        Assert.True(analysis.ObjectCounts.Values.Sum() > 0);
    }

    [Fact]
    public void ComputeBeatmapMd5_MatchesRawFileHash()
    {
        var calculator = new PpyOsuCalculator();
        var bytes = File.ReadAllBytes(FixturePath);
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(bytes));

        var md5 = calculator.ComputeBeatmapMd5(bytes);

        Assert.Equal(expected, md5, ignoreCase: true);
    }
}
