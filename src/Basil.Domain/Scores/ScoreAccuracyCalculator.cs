namespace Basil.Domain.Scores;

/// <summary>Ported from app/objects/score.py's Score.calculate_accuracy, per-vanilla-mode formulas.</summary>
public static class ScoreAccuracyCalculator
{
    public static double Calculate(int modeVanilla, int n300, int n100, int n50, int ngeki, int nkatu, int nmiss,
        Mods mods)
    {
        switch (modeVanilla)
        {
            case 0: // osu!
            {
                var total = n300 + n100 + n50 + nmiss;
                if (total == 0) return 0.0;

                return 100.0 * (n300 * 300.0 + n100 * 100.0 + n50 * 50.0) / (total * 300.0);
            }

            case 1: // osu!taiko
            {
                var total = n300 + n100 + nmiss;
                if (total == 0) return 0.0;

                return 100.0 * (n100 * 0.5 + n300) / total;
            }

            case 2: // osu!catch
            {
                var total = n300 + n100 + n50 + nkatu + nmiss;
                if (total == 0) return 0.0;

                return 100.0 * (n300 + n100 + n50) / total;
            }

            case 3: // osu!mania
            {
                var total = n300 + n100 + n50 + ngeki + nkatu + nmiss;
                if (total == 0) return 0.0;

                if ((mods & Mods.ScoreV2) != Mods.NoMod)
                    return 100.0 * (n50 * 50.0 + n100 * 100.0 + nkatu * 200.0 + n300 * 300.0 + ngeki * 305.0)
                           / (total * 305.0);

                return 100.0 * (n50 * 50.0 + n100 * 100.0 + nkatu * 200.0 + (n300 + ngeki) * 300.0) / (total * 300.0);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(modeVanilla), modeVanilla, "Invalid vanilla mode.");
        }
    }
}