using Basil.Domain.Beatmaps;

// ReSharper disable InconsistentNaming

namespace Basil.Domain.Scores;

/// <summary>
///     Represents the hit-judgment counts of a score.
/// </summary>
/// <param name="x300">The number of 300 judgments.</param>
/// <param name="x100">The number of 100 judgments.</param>
/// <param name="x50">The number of 50 judgments.</param>
/// <param name="xGeki">The number of geki judgments.</param>
/// <param name="xKatu">The number of katu judgments.</param>
/// <param name="xMiss">The number of miss judgments.</param>
public record HitCounts(int x300, int x100, int x50, int xGeki, int xKatu, int xMiss)
{
	/// <summary>
	///     Computes the accuracy percentage from the hit counts.
	/// </summary>
	/// <param name="mode">The game mode the accuracy is computed for.</param>
	/// <param name="mods">
	///     The mods applied to the play, used to select the mania scoring formula.
	/// </param>
	/// <returns>The accuracy as a percentage from 0 to 100.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="mode" /> is not a value of <see cref="GameMode" />.
	/// </exception>
	public double CalculateAccuracy(GameMode mode, Mods mods)
	{
		switch (mode)
		{
			case GameMode.Standard:
			{
				var total = x300 + x100 + x50 + xMiss;
				if (total == 0) return 0.0;

				return 100.0 * (x300 * 300.0 + x100 * 100.0 + x50 * 50.0) / (total * 300.0);
			}

			case GameMode.Taiko:
			{
				var total = x300 + x100 + xMiss;
				if (total == 0) return 0.0;

				return 100.0 * (x100 * 0.5 + x300) / total;
			}

			case GameMode.Catch:
			{
				var total = x300 + x100 + x50 + xKatu + xMiss;
				if (total == 0) return 0.0;

				return 100.0 * (x300 + x100 + x50) / total;
			}

			case GameMode.Mania:
			{
				var total = x300 + x100 + x50 + xGeki + xKatu + xMiss;
				if (total == 0) return 0.0;

				if ((mods & Mods.ScoreV2) != Mods.NoMod)
					return 100.0 *
						(x50 * 50.0 + x100 * 100.0 + xKatu * 200.0 + x300 * 300.0 + xGeki * 305.0) / (total * 305.0);

				return 100.0 *
					(x50 * 50.0 + x100 * 100.0 + xKatu * 200.0 + (x300 + xGeki) * 300.0) / (total * 300.0);
			}

			default:
				throw new ArgumentOutOfRangeException(nameof(GameMode), mode, "Invalid game mode.");
		}
	}
}
