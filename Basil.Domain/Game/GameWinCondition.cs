using Basil.Domain.Scores;
using Basil.Shared.Utilities;

namespace Basil.Domain.Game;

/// <summary>The measure that decides which play wins a match round; the values carry the game numbering.</summary>
public enum GameWinCondition : byte
{
	/// <summary>The highest total score wins.</summary>
	Score = 0,

	/// <summary>The highest accuracy wins.</summary>
	Accuracy = 1,

	/// <summary>The highest max combo wins.</summary>
	Combo = 2,

	/// <summary>The highest total score wins, with the plays scored under the ScoreV2 system.</summary>
	ScoreV2 = 3
}

/// <summary>Reads the properties of a <see cref="GameWinCondition"/>.</summary>
public static class GameWinConditionExtensions
{
	extension(GameWinCondition condition)
	{
		/// <summary>Gets an ordering that sorts the better play first under the win condition.</summary>
		/// <returns>
		/// A comparer that puts a passed play before a failed one, then the play with the higher measure first; ties fall back to total score, then to the lower score id. A missing score sorts before any present one.
		/// </returns>
		/// <exception cref="ArgumentOutOfRangeException">The win condition is not a defined value.</exception>
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

		/// <summary>Gets the value of one play under the win condition.</summary>
		/// <param name="score">The play to measure.</param>
		/// <returns>The total score for <see cref="GameWinCondition.Score"/> and <see cref="GameWinCondition.ScoreV2"/>, the accuracy in percent for <see cref="GameWinCondition.Accuracy"/>, or the max combo for <see cref="GameWinCondition.Combo"/>.</returns>
		/// <exception cref="ArgumentOutOfRangeException">The win condition is not a defined value.</exception>
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

		/// <summary>Combines the values of several plays into one value for a team.</summary>
		/// <param name="scores">The plays to combine.</param>
		/// <returns>The sum of the plays' values, for accuracy as for every other condition.</returns>
		public double CalculateMetricScores(IEnumerable<ScoreData> scores)
		{
			return scores.Sum(score => condition.GetMetricScore(score));
		}

		/// <summary>Tells whether two plays that tie on the win condition's value are separated by their total score.</summary>
		/// <param name="score">One of the tied plays.</param>
		/// <returns><see langword="true"/> for combo, and for accuracy when the tied accuracy is 100%; <see langword="false"/> otherwise, where a tie stays a tie.</returns>
		public bool BreaksTieByScore(ScoreData score)
		{
			return condition == GameWinCondition.Combo ||
			       (condition == GameWinCondition.Accuracy && score.Accuracy.NearlyEqual(100.0, 1e-9));
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