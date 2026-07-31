using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;

namespace Basil.Domain.Tests;

public class DifficultyModCalculatorTests
{
	[Theory]
	[InlineData(Mods.NoMod, 1.0)]
	[InlineData(Mods.HardRock, 1.0)]
	[InlineData(Mods.DoubleTime, 1.5)]
	[InlineData(Mods.Nightcore, 1.5)]
	[InlineData(Mods.DoubleTime | Mods.Nightcore, 1.5)]
	[InlineData(Mods.HalfTime, 0.75)]
	public void RateMultiplier_ReturnsExpectedRate(Mods mods, double expected)
	{
		Assert.Equal(expected, DifficultyModCalculator.RateMultiplier(mods));
	}

	[Fact]
	public void AdjustApproachRateForRate_NoRateChange_ReturnsInputUnchanged()
	{
		var result = DifficultyModCalculator.AdjustApproachRateForRate(9, 1.0);

		Assert.Equal(9, result);
	}

	[Fact]
	public void AdjustApproachRateForRate_DoubleTime_MatchesKnownReference()
	{
		// AR9 under DT (1.5x) is a commonly cited reference value: preempt 600ms -> 400ms -> AR10.33.
		var result = DifficultyModCalculator.AdjustApproachRateForRate(9, 1.5);

		Assert.Equal(10.33, result, 2);
	}

	[Fact]
	public void AdjustOverallDifficultyForRate_NoRateChange_ReturnsInputUnchanged()
	{
		var result = DifficultyModCalculator.AdjustOverallDifficultyForRate(8, 1.0);

		Assert.Equal(8, result);
	}

	[Fact]
	public void AdjustOverallDifficultyForRate_HalfTime_LowersEffectiveOd()
	{
		// HT (0.75x) widens hit windows, which lowers the OD value that reproduces them.
		var result = DifficultyModCalculator.AdjustOverallDifficultyForRate(8, 0.75);

		Assert.True(result < 8);
	}
}
