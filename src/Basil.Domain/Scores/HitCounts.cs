using Basil.Domain.Mechanics;

// ReSharper disable InconsistentNaming

namespace Basil.Domain.Scores;

/// <summary>
///     Represents the hit-judgment counts of a score.
/// </summary>
/// <param name="num300">The number of 300 judgments.</param>
/// <param name="num100">The number of 100 judgments.</param>
/// <param name="num50">The number of 50 judgments.</param>
/// <param name="numGeki">The number of geki judgments.</param>
/// <param name="numKatu">The number of katu judgments.</param>
/// <param name="numMiss">The number of miss judgments.</param>
#pragma warning disable IDE1006
public record HitCounts(int num300, int num100, int num50, int numGeki, int numKatu, int numMiss)
#pragma warning restore IDE1006
{
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
				var total = num300 + num100 + num50 + numMiss;
				if (total == 0) return 0.0;

				return 100.0 * (num300 * 300.0 + num100 * 100.0 + num50 * 50.0) / (total * 300.0);
			}

			case GameMode.Taiko:
			{
				var total = num300 + num100 + numMiss;
				if (total == 0) return 0.0;

				return 100.0 * (num100 * 0.5 + num300) / total;
			}

			case GameMode.Catch:
			{
				var total = num300 + num100 + num50 + numKatu + numMiss;
				if (total == 0) return 0.0;

				return 100.0 * (num300 + num100 + num50) / total;
			}

			case GameMode.Mania:
			{
				var total = num300 + num100 + num50 + numGeki + numKatu + numMiss;
				if (total == 0) return 0.0;

				if ((gameMods & GameMods.ScoreV2) != GameMods.NoMod)
					return 100.0 *
					       (num50 * 50.0 + num100 * 100.0 + numKatu * 200.0 + num300 * 300.0 + numGeki * 305.0) /
					       (total * 305.0);

				return 100.0 *
					(num50 * 50.0 + num100 * 100.0 + numKatu * 200.0 + (num300 + numGeki) * 300.0) / (total * 300.0);
			}

			default:
				throw new ArgumentOutOfRangeException(nameof(GameMode), mode, "Invalid game mode.");
		}
	}
}