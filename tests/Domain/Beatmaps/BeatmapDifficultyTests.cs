using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

namespace Basil.Domain.Tests.Beatmaps;

/// <summary>Verifies the validation of the gameplay settings of a difficulty.</summary>
public class BeatmapDifficultyTests
{
	private static BeatmapDifficulty Make(double bpm = 180.0, TimeSpan? length = null, double cs = 4.0, double ar = 9.0,
		double od = 8.0, double hp = 5.0, double star = 6.5)
	{
		return new BeatmapDifficulty(GameMode.Standard, bpm, length ?? TimeSpan.FromSeconds(120), cs, ar, od, hp, star);
	}

	[Theory]
	[InlineData(-1.0)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void Bpm_NotAPositiveNumber_Constructor_Throws(double bpm)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(bpm));
	}

	[Fact]
	public void Bpm_Negative_With_Throws()
	{
		var difficulty = Make();

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = difficulty with { Bpm = -1.0 });
	}

	[Fact]
	public void Length_Negative_Constructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(length: TimeSpan.FromSeconds(-1)));
	}

	[Fact]
	public void Length_Negative_With_Throws()
	{
		var difficulty = Make();

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = difficulty with { Length = TimeSpan.FromSeconds(-1) });
	}

	[Theory]
	[InlineData(-1.0)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void Star_NotAPositiveNumber_Constructor_Throws(double star)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(star: star));
	}

	[Fact]
	public void Star_Negative_With_Throws()
	{
		var difficulty = Make();

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = difficulty with { Star = -1.0 });
	}

	[Theory]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void Cs_NotFinite_Constructor_Throws(double cs)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(cs: cs));
	}

	[Theory]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void Ar_NotFinite_Constructor_Throws(double ar)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(ar: ar));
	}

	[Theory]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void Od_NotFinite_Constructor_Throws(double od)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(od: od));
	}

	[Theory]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void Hp_NotFinite_Constructor_Throws(double hp)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => Make(hp: hp));
	}

	[Fact]
	public void Ar_BelowZero_IsAccepted()
	{
		Assert.Equal(-1.0, Make(ar: -1.0).Ar);
	}

	[Fact]
	public void Mode_Undefined_Constructor_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new BeatmapDifficulty((GameMode)200, 180.0,
			TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 6.5));
	}
}