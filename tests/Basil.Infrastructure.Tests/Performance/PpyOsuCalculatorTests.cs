using System.Security.Cryptography;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Infrastructure.Performance;

namespace Basil.Infrastructure.Tests.Performance;

/// <summary>
///     Verifies the ppy.osu.Game-backed calculation engine produces stable star ratings and
///     hit-object counts for Fixtures/vivid_osu_file.osu across representative mod combinations.
///     Reference values were recorded by running this calculator directly — not a cross-check
///     against any other engine's output.
/// </summary>
public class PpyOsuCalculatorTests
{
	private static string FixturePath =>
		Path.Combine(AppContext.BaseDirectory, "Fixtures", "vivid_osu_file.osu");

	/// <summary>
	///     Expected values are the engine's raw (unrounded) output — <see cref="PpyOsuCalculator.Analyze" />
	///     rounds <c>Sr</c> to 2 decimals before returning it, so the assertion rounds the recorded
	///     reference the same way rather than hardcoding a second, hand-rounded literal.
	/// </summary>
	[Theory]
	[InlineData(Mods.NoMod, 4.8750450142072701)]
	[InlineData(Mods.HardRock, 5.9296060838721534)]
	[InlineData(Mods.DoubleTime, 7.0477415498633968)]
	[InlineData(Mods.Hidden | Mods.DoubleTime, 7.2215498802893343)]
	public void Analyze_StarRating_MatchesRecordedReference(Mods mods, double expectedRawStars)
	{
		var calculator = new PpyOsuCalculator();

		var analysis = calculator.Analyze(FixturePath, GameMode.Standard, mods);

		var expectedRounded = Math.Round(expectedRawStars, 2, MidpointRounding.AwayFromZero);
		Assert.Equal(expectedRounded, analysis.Difficulty.Sr, 10);
	}

	/// <summary>
	///     Regression lock for CS/AR/OD/HP now coming from <c>Ruleset.GetAdjustedDisplayDifficulty</c>
	///     instead of a hand-rolled formula — DoubleTime/HalfTime values below match what the deleted
	///     <c>DifficultyModCalculator</c> formula used to produce for this fixture's base AR/OD (7/7),
	///     confirming the library call is a drop-in replacement for Standard.
	/// </summary>
	[Theory]
	[InlineData(Mods.NoMod, 6, 7, 7, 2)]
	[InlineData(Mods.DoubleTime, 6, 9, 9.1, 2)]
	[InlineData(Mods.HalfTime, 6, 5, 4.9, 2)]
	[InlineData(Mods.HardRock, 7.8, 9.8, 9.8, 2.8)]
	public void Analyze_StandardDifficultyStats_MatchesRecordedReference(
		Mods mods, double expectedCs, double expectedAr, double expectedOd, double expectedHp)
	{
		var calculator = new PpyOsuCalculator();

		var analysis = calculator.Analyze(FixturePath, GameMode.Standard, mods);

		Assert.Equal(expectedCs, analysis.Difficulty.Cs, 5);
		Assert.Equal(expectedAr, analysis.Difficulty.Ar, 5);
		Assert.Equal(expectedOd, analysis.Difficulty.Od, 5);
		Assert.Equal(expectedHp, analysis.Difficulty.Hp, 5);
	}

	/// <summary>
	///     Regression lock for the other 3 rulesets' own <c>GetAdjustedDisplayDifficulty</c> semantics —
	///     Taiko only rate-adjusts OD (not AR), Catch only rate-adjusts AR (not OD), and Mania doesn't
	///     rate-adjust either (mania hit windows are constant-ms, not clock-rate-relative).
	/// </summary>
	[Theory]
	[InlineData(GameMode.Taiko, Mods.DoubleTime, 6, 7, 10.2)]
	[InlineData(GameMode.Catch, Mods.DoubleTime, 6, 9, 7)]
	[InlineData(GameMode.Mania, Mods.DoubleTime, 7, 7, 7)]
	public void Analyze_NonStandardDifficultyStats_MatchesRecordedReference(
		GameMode mode, Mods mods, double expectedCs, double expectedAr, double expectedOd)
	{
		var calculator = new PpyOsuCalculator();

		var analysis = calculator.Analyze(FixturePath, mode, mods);

		Assert.Equal(expectedCs, analysis.Difficulty.Cs, 5);
		Assert.Equal(expectedAr, analysis.Difficulty.Ar, 5);
		Assert.Equal(expectedOd, analysis.Difficulty.Od, 5);
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

		var osuCounts = Assert.IsType<OsuBeatmapObjectCounts>(analysis.ObjectCounts);
		Assert.True(osuCounts.Total > 0);
		Assert.Equal(osuCounts.Circles + osuCounts.Sliders + osuCounts.Spinners, osuCounts.Total);
	}

	/// <summary>
	///     Regression lock for the 3.1 fix: the raw `.osu` decoder never populates
	///     BeatmapInfo.Length/MaxCombo/BPM (confirmed zero/null both before AND after
	///     GetPlayableBeatmap by direct inspection) — these values only come from
	///     BeatmapExtensions.CalculatePlayableLength/GetMaxCombo/GetMostCommonBeatLength on the
	///     processed beatmap, which is what Analyze now returns instead of the always-zero raw fields.
	/// </summary>
	[Fact]
	public void Analyze_StandardFixture_ReturnsNonZeroLengthComboAndBpm()
	{
		var calculator = new PpyOsuCalculator();

		var analysis = calculator.Analyze(FixturePath, GameMode.Standard, Mods.NoMod);

		Assert.Equal(TimeSpan.FromMilliseconds(13742), analysis.Difficulty.TotalLength);
		Assert.Equal(114, analysis.ObjectCounts.MaxCombo);
		Assert.Equal(168.0, analysis.Difficulty.Bpm, 5);
	}

	/// <summary>
	///     osu!std's own object-count fixture converts cleanly to the other 3 rulesets (osu!'s auto
	///     convert), so this is the only fixture available to cover Taiko/Catch/Mania counting — unlike
	///     Standard, these numbers aren't independently cross-checked against a second source, just
	///     locked against what this converter's own output was at the time the test was written.
	/// </summary>
	[Fact]
	public void Analyze_TaikoConvertedFixture_CountsTopLevelHitTypes()
	{
		var calculator = new PpyOsuCalculator();

		var analysis = calculator.Analyze(FixturePath, GameMode.Taiko, Mods.NoMod);

		var taikoCounts = Assert.IsType<TaikoBeatmapObjectCounts>(analysis.ObjectCounts);
		Assert.Equal(114, taikoCounts.Hits);
		Assert.Equal(0, taikoCounts.DrumRolls);
		Assert.Equal(0, taikoCounts.Dendens);
		Assert.Equal(114, taikoCounts.Total);
		Assert.Equal(114, taikoCounts.MaxCombo);
	}

	/// <summary>
	///     Catch is the one mode where top-level hit objects alone don't give the right breakdown —
	///     JuiceStream/BananaShower are containers whose Droplet/TinyDroplet/Banana children only exist
	///     nested, so <c>PpyOsuCalculator</c> recurses <c>NestedHitObjects</c> for this mode specifically.
	///     These numbers are the regression lock for that recursion actually running.
	/// </summary>
	[Fact]
	public void Analyze_CatchConvertedFixture_CountsRecurseIntoNestedDroplets()
	{
		var calculator = new PpyOsuCalculator();

		var analysis = calculator.Analyze(FixturePath, GameMode.Catch, Mods.NoMod);

		var catchCounts = Assert.IsType<CatchBeatmapObjectCounts>(analysis.ObjectCounts);
		Assert.Equal(112, catchCounts.Fruits);
		Assert.Equal(2, catchCounts.Droplets);
		Assert.Equal(6, catchCounts.TinyDroplets);
		Assert.Equal(0, catchCounts.Bananas);
		Assert.Equal(120, catchCounts.Total);
	}

	[Fact]
	public void Analyze_ManiaConvertedFixture_CountsTopLevelNoteTypes()
	{
		var calculator = new PpyOsuCalculator();

		var analysis = calculator.Analyze(FixturePath, GameMode.Mania, Mods.NoMod);

		var maniaCounts = Assert.IsType<ManiaBeatmapObjectCounts>(analysis.ObjectCounts);
		Assert.Equal(139, maniaCounts.Notes);
		Assert.Equal(7, maniaCounts.HoldNotes);
		Assert.Equal(146, maniaCounts.Total);
		Assert.Equal(153, maniaCounts.MaxCombo);
	}

	[Fact]
	public void ComputeBeatmapMd5_MatchesRawFileHash()
	{
		var calculator = new PpyOsuCalculator();
		var bytes = File.ReadAllBytes(FixturePath);
		var expected = Convert.ToHexStringLower(MD5.HashData(bytes));

		var md5 = calculator.ComputeBeatmapMd5(bytes);

		Assert.Equal(expected, md5, true);
	}
}