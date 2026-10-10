using Basil.Domain.Scores;

namespace Basil.Domain.Mechanics;

/// <summary>
///     Specifies how the winner of a multiplayer match is decided.
/// </summary>
public enum GameWinCondition : byte
{
	/// <summary>The match is decided by the total score of each team.</summary>
	Score = 0,

	/// <summary>The match is decided by the accuracy of each team.</summary>
	Accuracy = 1,

	/// <summary>The match is decided by the combo of each team.</summary>
	Combo = 2,

	/// <summary>The match is decided by the ScoreV2 scoring rules.</summary>
	ScoreV2 = 3
}

/// <summary>Provides the ranking a win condition imposes on scores.</summary>
public static class GameWinConditionExtensions
{
	/// <param name="condition">The win condition that selects the metric.</param>
	extension(GameWinCondition condition)
	{
		/// <summary>Gets the comparer that orders scores by this win condition's metric.</summary>
		/// <returns>
		///     A comparer in the metric's natural order: a score with the better metric compares greater, so
		///     ordering descending puts the winner first.
		/// </returns>
		/// <exception cref="ArgumentOutOfRangeException">The win condition is not a defined value.</exception>
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

		/// <summary>Gets the metric this win condition compares.</summary>
		/// <param name="score">The score to measure.</param>
		/// <returns>The total score, the accuracy, or the combo count at the end of the beatmap.</returns>
		public double Metric(ScoreData score)
		{
			return condition switch
			{
				GameWinCondition.Accuracy => score.Accuracy,
				GameWinCondition.Combo => score.CurrentCombo,
				_ => score.TotalScore
			};
		}
	}

	/// <summary>Compares scores by total score; a higher score compares greater.</summary>
	private sealed class ScoreComparer : IComparer<ScoreData>
	{
		private ScoreComparer()
		{
		}

		/// <summary>Gets the single instance of the comparer.</summary>
		public static readonly ScoreComparer Instance = new();

		/// <inheritdoc />
		public int Compare(ScoreData? x, ScoreData? y)
		{
			return Nulls(x, y) ?? x!.TotalScore.CompareTo(y!.TotalScore);
		}
	}

	/// <summary>
	///     Compares scores by accuracy; a higher accuracy compares greater. A perfect-accuracy tie is broken
	///     by total score, any other tie is a draw.
	/// </summary>
	private sealed class AccuracyComparer : IComparer<ScoreData>
	{
		private AccuracyComparer()
		{
		}

		/// <summary>Gets the single instance of the comparer.</summary>
		public static readonly AccuracyComparer Instance = new();

		/// <inheritdoc />
		public int Compare(ScoreData? x, ScoreData? y)
		{
			if (Nulls(x, y) is { } order) return order;
			var byAccuracy = x!.Accuracy.CompareTo(y!.Accuracy);

			// Not Equals
			if (byAccuracy != 0) return byAccuracy;

			// Equals
			var isPerfect = x.Accuracy >= 1.0;
			return isPerfect ? ScoreComparer.Instance.Compare(x, y) : 0; // osu! domain, do not change!
		}
	}

	/// <summary>
	///     Compares scores by the combo count at the end of the beatmap; a higher count compares greater. A
	///     tie is broken by total score.
	/// </summary>
	private sealed class ComboComparer : IComparer<ScoreData>
	{
		private ComboComparer()
		{
		}

		/// <summary>Gets the single instance of the comparer.</summary>
		public static readonly ComboComparer Instance = new();

		/// <inheritdoc />
		public int Compare(ScoreData? x, ScoreData? y)
		{
			if (Nulls(x, y) is { } order) return order;

			var byCombo = x!.CurrentCombo.CompareTo(y!.CurrentCombo);
			return byCombo != 0 ? byCombo : ScoreComparer.Instance.Compare(x, y);
		}
	}

	/// <summary>Orders two possibly null scores by presence, or returns nothing when both are present.</summary>
	private static int? Nulls(ScoreData? x, ScoreData? y)
	{
		if (ReferenceEquals(x, y)) return 0;
		if (x is null) return 1;
		if (y is null) return -1;
		return null;
	}
}