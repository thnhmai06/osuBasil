using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Domain.Tests.Mechanics;

/// <summary>Verifies how a win condition orders the scores of a match.</summary>
public class GameWinConditionTests
{
	private static readonly HitCounts Perfect = new(100, 0, 0, 0, 0, 0);

	private static readonly HitCounts NearlyPerfect = new(99, 0, 0, 0, 0, 1);

	private static ScoreData Make(HitCounts hits, int totalScore, short maxCombo, short currentCombo)
	{
		return new ScoreData(null, null, GameMode.Standard, GameMods.NoMod, hits, totalScore, maxCombo, currentCombo,
			Grade.S, true, false, DateTimeOffset.UnixEpoch);
	}

	[Fact]
	public void AccuracyComparer_PerfectTie_BrokenByTotalScore()
	{
		var comparer = GameWinCondition.Accuracy.ToComparer();
		var lower = Make(Perfect, 1000, 10, 10);
		var higher = Make(Perfect, 2000, 10, 10);

		Assert.True(comparer.Compare(lower, higher) < 0);
		Assert.True(comparer.Compare(higher, lower) > 0);
	}

	[Fact]
	public void AccuracyComparer_TieBelowPerfect_IsDraw()
	{
		var comparer = GameWinCondition.Accuracy.ToComparer();
		var lower = Make(NearlyPerfect, 1000, 10, 10);
		var higher = Make(NearlyPerfect, 2000, 10, 10);

		Assert.Equal(0, comparer.Compare(lower, higher));
		Assert.Equal(0, comparer.Compare(higher, lower));
	}

	[Fact]
	public void AccuracyComparer_DifferentAccuracy_OrdersByAccuracy()
	{
		var comparer = GameWinCondition.Accuracy.ToComparer();
		var perfect = Make(Perfect, 1000, 10, 10);
		var nearly = Make(NearlyPerfect, 5000, 10, 10);

		Assert.True(comparer.Compare(nearly, perfect) < 0);
		Assert.True(comparer.Compare(perfect, nearly) > 0);
	}

	[Fact]
	public void ComboComparer_HigherMaxCombo_ComparesGreater()
	{
		var comparer = GameWinCondition.Combo.ToComparer();
		var higher = Make(Perfect, 1000, 500, 10);
		var lower = Make(Perfect, 5000, 100, 200);

		Assert.True(comparer.Compare(lower, higher) < 0);
		Assert.True(comparer.Compare(higher, lower) > 0);
	}

	[Fact]
	public void ComboComparer_EqualMaxCombo_TieBrokenByTotalScore()
	{
		var comparer = GameWinCondition.Combo.ToComparer();
		var lower = Make(Perfect, 1000, 500, 10);
		var higher = Make(Perfect, 2000, 500, 10);

		Assert.True(comparer.Compare(lower, higher) < 0);
		Assert.True(comparer.Compare(higher, lower) > 0);
	}

	[Fact]
	public void Metric_Combo_IsHighestComboReached()
	{
		var score = Make(Perfect, 1000, 500, 10);

		Assert.Equal(500, GameWinCondition.Combo.Metric(score));
	}

	[Fact]
	public void Metric_Accuracy_IsAccuracyPercentage()
	{
		var score = Make(NearlyPerfect, 1000, 10, 10);

		Assert.Equal(99.0, GameWinCondition.Accuracy.Metric(score), 10);
	}

	[Fact]
	public void Metric_Score_IsTotalScore()
	{
		var score = Make(Perfect, 1234, 10, 10);

		Assert.Equal(1234, GameWinCondition.Score.Metric(score));
	}

	[Fact]
	public void Metric_UndefinedCondition_Throws()
	{
		var score = Make(Perfect, 1000, 10, 10);

		Assert.Throws<ArgumentOutOfRangeException>(() => ((GameWinCondition)200).Metric(score));
	}

	[Fact]
	public void ToComparer_UndefinedCondition_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => ((GameWinCondition)200).ToComparer());
	}
}