using Basil.Domain.Mechanics;

// ReSharper disable InconsistentNaming

namespace Basil.Domain.Scores;

/// <summary>
///     Represents the hit-judgment counts of a score.
/// </summary>
/// <param name="Num300">The number of 300 judgments.</param>
/// <param name="Num100">The number of 100 judgments.</param>
/// <param name="Num50">The number of 50 judgments.</param>
/// <param name="NumGeki">The number of geki judgments.</param>
/// <param name="NumKatu">The number of katu judgments.</param>
/// <param name="NumMiss">The number of miss judgments.</param>
/// <exception cref="ArgumentOutOfRangeException">
///     Any count is negative.
/// </exception>
public readonly record struct HitCounts(int Num300, int Num100, int Num50, int NumGeki, int NumKatu, int NumMiss)
{
	/// <summary>
	///     Gets the number of 300 judgments.
	/// </summary>
	public int Num300 { get; init; } = Num300 >= 0
		? Num300
		: throw new ArgumentOutOfRangeException(nameof(Num300), "Hit count cannot be negative.");

	/// <summary>
	///     Gets the number of 100 judgments.
	/// </summary>
	public int Num100 { get; init; } = Num100 >= 0
		? Num100
		: throw new ArgumentOutOfRangeException(nameof(Num100), "Hit count cannot be negative.");

	/// <summary>
	///     Gets the number of 50 judgments.
	/// </summary>
	public int Num50 { get; init; } = Num50 >= 0
		? Num50
		: throw new ArgumentOutOfRangeException(nameof(Num50), "Hit count cannot be negative.");

	/// <summary>
	///     Gets the number of geki judgments.
	/// </summary>
	public int NumGeki { get; init; } = NumGeki >= 0
		? NumGeki
		: throw new ArgumentOutOfRangeException(nameof(NumGeki), "Hit count cannot be negative.");

	/// <summary>
	///     Gets the number of katu judgments.
	/// </summary>
	public int NumKatu { get; init; } = NumKatu >= 0
		? NumKatu
		: throw new ArgumentOutOfRangeException(nameof(NumKatu), "Hit count cannot be negative.");

	/// <summary>
	///     Gets the number of miss judgments.
	/// </summary>
	public int NumMiss { get; init; } = NumMiss >= 0
		? NumMiss
		: throw new ArgumentOutOfRangeException(nameof(NumMiss), "Hit count cannot be negative.");

	/// <summary>
	///     Computes the accuracy percentage from the hit counts.
	/// </summary>
	/// <param name="mode">The game mode the accuracy is computed for.</param>
	/// <param name="gameMods">
	///     The mods applied to the play, used to select the mania scoring formula.
	/// </param>
	/// <returns>The accuracy as a percentage from 0 to 100.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="mode" /> is not a value of <see cref="GameMode" />.
	/// </exception>
	public double CalculateAccuracy(GameMode mode, GameMods gameMods)
	{
		switch (mode)
		{
			case GameMode.Standard:
			{
				var total = Num300 + Num100 + Num50 + NumMiss;
				if (total == 0) return 0.0;

				return 100.0 * (Num300 * 300.0 + Num100 * 100.0 + Num50 * 50.0) / (total * 300.0);
			}

			case GameMode.Taiko:
			{
				var total = Num300 + Num100 + NumMiss;
				if (total == 0) return 0.0;

				return 100.0 * (Num100 * 0.5 + Num300) / total;
			}

			case GameMode.Catch:
			{
				var total = Num300 + Num100 + Num50 + NumKatu + NumMiss;
				if (total == 0) return 0.0;

				return 100.0 * (Num300 + Num100 + Num50) / total;
			}

			case GameMode.Mania:
			{
				var total = Num300 + Num100 + Num50 + NumGeki + NumKatu + NumMiss;
				if (total == 0) return 0.0;

				if ((gameMods & GameMods.ScoreV2) != GameMods.NoMod)
					return 100.0 *
					       (Num50 * 50.0 + Num100 * 100.0 + NumKatu * 200.0 + Num300 * 300.0 + NumGeki * 305.0) /
					       (total * 305.0);

				return 100.0 *
					(Num50 * 50.0 + Num100 * 100.0 + NumKatu * 200.0 + (Num300 + NumGeki) * 300.0) / (total * 300.0);
			}

			default:
				throw new ArgumentOutOfRangeException(nameof(GameMode), mode, "Invalid game mode.");
		}
	}
}