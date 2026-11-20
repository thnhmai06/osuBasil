using Basil.Domain.Game;

// ReSharper disable InconsistentNaming

namespace Basil.Domain.Scores;

/// <summary>How many hits of each judgement a play earned.</summary>
/// <param name="Num300">The top judgement (300 points): a great in taiko, a fruit in catch.</param>
/// <param name="Num100">The 100-point judgement: a good in taiko, a large droplet in catch.</param>
/// <param name="Num50">The 50-point judgement: a tiny droplet caught in catch.</param>
/// <param name="NumGeki">The perfect 300 (MAX) of mania, and the 300 that completes a section in standard.</param>
/// <param name="NumKatu">The 200-point judgement of mania, and the 100 that completes a section in standard; the missed tiny droplets in catch.</param>
/// <param name="NumMiss">The misses.</param>
/// <exception cref="ArgumentOutOfRangeException">A count is negative.</exception>
public readonly record struct HitCounts(int Num300, int Num100, int Num50, int NumGeki, int NumKatu, int NumMiss)
{
	/// <summary>The count of the top judgement.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
	public int Num300
	{
		get;
		init => field = Check(value, nameof(Num300));
	} = Check(Num300, nameof(Num300));

	/// <summary>The count of the 100-point judgement.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
	public int Num100
	{
		get;
		init => field = Check(value, nameof(Num100));
	} = Check(Num100, nameof(Num100));

	/// <summary>The count of the 50-point judgement.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
	public int Num50
	{
		get;
		init => field = Check(value, nameof(Num50));
	} = Check(Num50, nameof(Num50));

	/// <summary>The count of the geki judgement.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
	public int NumGeki
	{
		get;
		init => field = Check(value, nameof(NumGeki));
	} = Check(NumGeki, nameof(NumGeki));

	/// <summary>The count of the katu judgement.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
	public int NumKatu
	{
		get;
		init => field = Check(value, nameof(NumKatu));
	} = Check(NumKatu, nameof(NumKatu));

	/// <summary>The count of misses.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
	public int NumMiss
	{
		get;
		init => field = Check(value, nameof(NumMiss));
	} = Check(NumMiss, nameof(NumMiss));

	private static int Check(int value, string name)
	{
		return value >= 0
			? value
			: throw new ArgumentOutOfRangeException(name, "Hit count cannot be negative.");
	}

	/// <summary>Calculates the accuracy of the play under the rules of its mode.</summary>
	/// <param name="mode">The game mode the play was made in.</param>
	/// <param name="gameMods">The mods of the play; <see cref="GameMods.ScoreV2"/> changes how mania weighs its judgements.</param>
	/// <returns>The accuracy as a percentage from 0 to 100; 0 when there are no hits to judge.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined mode.</exception>
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
				throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid game mode.");
		}
	}
}