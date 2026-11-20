using Basil.Domain.Scores;
using Basil.Shared.Utilities;

namespace Basil.Domain.Game;

public enum GameWinCondition : byte
{
	Score = 0,

	Accuracy = 1,

	Combo = 2,

	ScoreV2 = 3
}

public static class GameWinConditionExtensions
{
	extension(GameWinCondition condition)
	{
		public IComparer<ScoreRecord> ToComparer()
		{
			return condition switch
			{
				GameWinCondition.Score or GameWinCondition.ScoreV2 => ScoreComparer.Instance,
				GameWinCondition.Accuracy => AccuracyComparer.Instance,
				GameWinCondition.Combo => ComboComparer.Instance,
				_ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown win condition.")
			};
		}

		public double GetMetricScore(ScoreData score)
		{
			return condition switch
			{
				GameWinCondition.Score or GameWinCondition.ScoreV2 => score.LastState.TotalScore,
				GameWinCondition.Accuracy => score.Accuracy,
				GameWinCondition.Combo => score.LastState.MaxCombo,
				_ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown win condition.")
			};
		}

		public double CalculateMetricScores(IEnumerable<ScoreData> scores)
		{
			return condition == GameWinCondition.Accuracy
				? scores.Average(score => condition.GetMetricScore(score))
				: scores.Sum(score => condition.GetMetricScore(score));
		}
	}

	private static int? Nulls(ScoreRecord? x, ScoreRecord? y)
	{
		if (ReferenceEquals(x, y)) return 0;
		if (x is null) return -1;
		if (y is null) return 1;
		return null;
	}

	private sealed class ScoreComparer : IComparer<ScoreRecord>
	{
		public static readonly ScoreComparer Instance = new();

		private ScoreComparer()
		{
		}

		public int Compare(ScoreRecord? x, ScoreRecord? y)
		{
			if (Nulls(x, y) is { } order) return order;

			var byPassed = y!.Value.IsPassed.CompareTo(x!.Value.IsPassed);
			if (byPassed != 0) return byPassed;

			var byScore = y.Value.LastState.TotalScore.CompareTo(x.Value.LastState.TotalScore);
			return byScore != 0 ? byScore : x.Id.CompareTo(y.Id);
		}
	}

	private sealed class AccuracyComparer : IComparer<ScoreRecord>
	{
		public static readonly AccuracyComparer Instance = new();

		private AccuracyComparer()
		{
		}

		public int Compare(ScoreRecord? x, ScoreRecord? y)
		{
			if (Nulls(x, y) is { } order) return order;

			var byPassed = y!.Value.IsPassed.CompareTo(x!.Value.IsPassed);
			if (byPassed != 0) return byPassed;

			var byAccuracy = y.Value.Accuracy.CompareTo(x.Value.Accuracy);

			// Not Equals
			if (byAccuracy != 0) return byAccuracy;

			// Equals
			var isPerfect = x.Value.Accuracy.NearlyEqual(100.0, 1e-9);
			return isPerfect
				? ScoreComparer.Instance.Compare(x, y)
				: x.Id.CompareTo(y.Id);
		}
	}

	private sealed class ComboComparer : IComparer<ScoreRecord>
	{
		public static readonly ComboComparer Instance = new();

		private ComboComparer()
		{
		}

		public int Compare(ScoreRecord? x, ScoreRecord? y)
		{
			if (Nulls(x, y) is { } order) return order;

			var byPassed = y!.Value.IsPassed.CompareTo(x!.Value.IsPassed);
			if (byPassed != 0) return byPassed;

			var byCombo = y.Value.LastState.MaxCombo.CompareTo(x.Value.LastState.MaxCombo);
			return byCombo != 0 ? byCombo : ScoreComparer.Instance.Compare(x, y);
		}
	}
}