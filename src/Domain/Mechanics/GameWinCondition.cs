using Basil.Domain.Scores;
using Basil.Domain.Shared;

namespace Basil.Domain.Mechanics;

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
		public IComparer<ScoreData> ToComparer()
		{
			return condition switch
			{
				GameWinCondition.Score or GameWinCondition.ScoreV2 => ScoreComparer.Instance,
				GameWinCondition.Accuracy => AccuracyComparer.Instance,
				GameWinCondition.Combo => ComboComparer.Instance,
				_ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown win condition.")
			};
		}

		public double Metric(ScoreData score)
		{
			return condition switch
			{
				GameWinCondition.Score or GameWinCondition.ScoreV2 => score.LastState.TotalScore,
				GameWinCondition.Accuracy => score.Accuracy,
				GameWinCondition.Combo => score.LastState.MaxCombo,
				_ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown win condition.")
			};
		}
	}

	private static int? Nulls(ScoreData? x, ScoreData? y)
	{
		if (ReferenceEquals(x, y)) return 0;
		if (x is null) return -1;
		if (y is null) return 1;
		return null;
	}

	private sealed class ScoreComparer : IComparer<ScoreData>
	{
		public static readonly ScoreComparer Instance = new();

		private ScoreComparer()
		{
		}

		public int Compare(ScoreData? x, ScoreData? y)
		{
			return Nulls(x, y) ?? x!.LastState.TotalScore.CompareTo(y!.LastState.TotalScore);
		}
	}

	private sealed class AccuracyComparer : IComparer<ScoreData>
	{
		public static readonly AccuracyComparer Instance = new();

		private AccuracyComparer()
		{
		}

		public int Compare(ScoreData? x, ScoreData? y)
		{
			if (Nulls(x, y) is { } order) return order;
			var byAccuracy = x!.Accuracy.CompareTo(y!.Accuracy);

			// Not Equals
			if (byAccuracy != 0) return byAccuracy;

			// Equals
			var isPerfect = x.Accuracy.NearlyEqual(100.0, 1e-9);
			return isPerfect ? ScoreComparer.Instance.Compare(x, y) : 0; // osu! domain, do not change!
		}
	}

	private sealed class ComboComparer : IComparer<ScoreData>
	{
		public static readonly ComboComparer Instance = new();

		private ComboComparer()
		{
		}

		public int Compare(ScoreData? x, ScoreData? y)
		{
			if (Nulls(x, y) is { } order) return order;

			var byCombo = x!.LastState.MaxCombo.CompareTo(y!.LastState.MaxCombo);
			return byCombo != 0 ? byCombo : ScoreComparer.Instance.Compare(x, y);
		}
	}
}