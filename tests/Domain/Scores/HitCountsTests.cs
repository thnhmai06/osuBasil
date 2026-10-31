using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Domain.Tests.Scores;

public class HitCountsTests
{
	[Fact]
	public void Osu_AllTypesCounted()
	{
		var hits = new HitCounts(90, 5, 3, 0, 0, 2);
		var acc = hits.CalculateAccuracy(GameMode.Standard, GameMods.NoMod);

		const double expected = 100.0 * (90 * 300.0 + 5 * 100.0 + 3 * 50.0) / (100 * 300.0);
		Assert.Equal(expected, acc, 10);
	}

	[Fact]
	public void Osu_NoObjectsHit_ReturnsZero()
	{
		var hits = new HitCounts(0, 0, 0, 0, 0, 0);
		Assert.Equal(0.0, hits.CalculateAccuracy(GameMode.Standard, GameMods.NoMod));
	}

	[Fact]
	public void Taiko_UsesHalfWeightFor100s()
	{
		var hits = new HitCounts(80, 10, 0, 0, 0, 10);
		var acc = hits.CalculateAccuracy(GameMode.Taiko, GameMods.NoMod);

		const double expected = 100.0 * (10 * 0.5 + 80) / 100;
		Assert.Equal(expected, acc, 10);
	}

	[Fact]
	public void Catch_CountsFruitsAndDropletsOnly()
	{
		var hits = new HitCounts(90, 5, 3, 0, 1, 1);
		var acc = hits.CalculateAccuracy(GameMode.Catch, GameMods.NoMod);

		const double expected = 100.0 * (90 + 5 + 3) / 100;
		Assert.Equal(expected, acc, 10);
	}

	[Fact]
	public void Mania_WithoutScoreV2_TreatsGekiAsPerfect300()
	{
		var hits = new HitCounts(50, 20, 10, 15, 5, 0);
		var acc = hits.CalculateAccuracy(GameMode.Mania, GameMods.NoMod);

		const int total = 50 + 20 + 10 + 15 + 5;
		const double expected = 100.0 * (10 * 50.0 + 20 * 100.0 + 5 * 200.0 + (50 + 15) * 300.0) / (total * 300.0);
		Assert.Equal(expected, acc, 10);
	}

	[Fact]
	public void Mania_WithScoreV2_WeighsGekiHigherThanPerfect300()
	{
		var hits = new HitCounts(50, 20, 10, 15, 5, 0);
		var acc = hits.CalculateAccuracy(GameMode.Mania, GameMods.ScoreV2);

		const int total = 50 + 20 + 10 + 15 + 5;
		const double expected =
			100.0 * (10 * 50.0 + 20 * 100.0 + 5 * 200.0 + 50 * 300.0 + 15 * 305.0) / (total * 305.0);
		Assert.Equal(expected, acc, 10);
	}

	[Fact]
	public void InvalidMode_Throws()
	{
		var hits = new HitCounts(0, 0, 0, 0, 0, 0);
		Assert.Throws<ArgumentOutOfRangeException>(() => hits.CalculateAccuracy((GameMode)4, GameMods.NoMod));
	}

	[Theory]
	[InlineData(-1, 0, 0, 0, 0, 0)]
	[InlineData(0, -1, 0, 0, 0, 0)]
	[InlineData(0, 0, -1, 0, 0, 0)]
	[InlineData(0, 0, 0, -1, 0, 0)]
	[InlineData(0, 0, 0, 0, -1, 0)]
	[InlineData(0, 0, 0, 0, 0, -1)]
	public void NegativeCount_Constructor_Throws(int n300, int n100, int n50, int geki, int katu, int miss)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new HitCounts(n300, n100, n50, geki, katu, miss));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(-100)]
	public void NegativeCount_With_Throws(int negative)
	{
		var hits = new HitCounts(1, 2, 3, 4, 5, 6);

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = hits with { Num300 = negative });
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = hits with { Num100 = negative });
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = hits with { Num50 = negative });
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = hits with { NumGeki = negative });
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = hits with { NumKatu = negative });
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = hits with { NumMiss = negative });
	}
}