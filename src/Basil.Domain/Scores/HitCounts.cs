using Basil.Domain.Beatmaps;
// ReSharper disable InconsistentNaming

namespace Basil.Domain.Scores;

public record HitCounts(int x300, int x100, int x50, int xGeki, int xKatu, int xMiss)
{
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