using Basil.Domain.Game.Mechanics;

// ReSharper disable InconsistentNaming

namespace Basil.Domain.Game.Scores;

public readonly record struct HitCounts(int Num300, int Num100, int Num50, int NumGeki, int NumKatu, int NumMiss)
{
	public int Num300
	{
		get;
		init => field = Check(value, nameof(Num300));
	} = Check(Num300, nameof(Num300));

	public int Num100
	{
		get;
		init => field = Check(value, nameof(Num100));
	} = Check(Num100, nameof(Num100));

	public int Num50
	{
		get;
		init => field = Check(value, nameof(Num50));
	} = Check(Num50, nameof(Num50));

	public int NumGeki
	{
		get;
		init => field = Check(value, nameof(NumGeki));
	} = Check(NumGeki, nameof(NumGeki));

	public int NumKatu
	{
		get;
		init => field = Check(value, nameof(NumKatu));
	} = Check(NumKatu, nameof(NumKatu));

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