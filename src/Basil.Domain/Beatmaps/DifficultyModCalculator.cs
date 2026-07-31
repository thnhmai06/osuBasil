using Basil.Domain.Scores;

namespace Basil.Domain.Beatmaps;

/// <summary>
///     Rate-scaling math for DT/HT/NC — the one piece of mod-driven difficulty adjustment
///     osu!lazer's own libraries don't compute for us headlessly. HR/EZ's CS/AR/OD/HP multiplier is
///     already applied by <c>WorkingBeatmap.GetPlayableBeatmap(ruleset, mods)</c> itself (confirmed by
///     direct inspection: <c>playable.Difficulty</c> already reflects HR/EZ), so
///     <c>PpyOsuCalculator.Analyze</c> reads CS/AR/OD/HP straight off that — this class only covers what
///     that call leaves untouched: DT/HT/NC change playback rate, which
///     <c>GetPlayableBeatmap</c> never bakes into the beatmap model at all (BPM/length stay identical to
///     NoMod, confirmed by direct inspection), and osu!std's AR/OD are themselves real time windows that
///     shift with rate. These are the standard, documented osu! formulas (osu! wiki's Approach
///     rate/Overall difficulty pages) — the same precedent as <c>HitCounts.CalculateAccuracy</c>: a
///     standard formula with no equivalent library function, kept in <c>Basil.Domain</c> since it has
///     zero package dependencies by design.
/// </summary>
public static class DifficultyModCalculator
{
	/// <summary>DT/NC play back at 1.5x speed, HT at 0.75x — every other mod leaves the rate at 1x.</summary>
	public static double RateMultiplier(Mods mods)
	{
		if ((mods & (Mods.DoubleTime | Mods.Nightcore)) != Mods.NoMod) return 1.5;
		if ((mods & Mods.HalfTime) != Mods.NoMod) return 0.75;
		return 1.0;
	}

	/// <summary>
	///     Re-derives AR from its real preempt-time window scaled by <paramref name="rate" /> — only
	///     meaningful for osu!std, whose hit-window formulas don't apply to the other rulesets (each
	///     defines its own, not implemented here). Callers should leave AR/OD unadjusted outside
	///     Standard rather than call this.
	/// </summary>
	public static double AdjustApproachRateForRate(double ar, double rate)
	{
		return rate == 1.0 ? ar : ArFromPreemptMs(PreemptMsFromAr(ar) / rate);
	}

	/// <summary>Re-derives OD from its real "300" hit-window scaled by <paramref name="rate" /> — osu!std only, see <see cref="AdjustApproachRateForRate" />.</summary>
	public static double AdjustOverallDifficultyForRate(double od, double rate)
	{
		return rate == 1.0 ? od : OdFromHitWindowMs(HitWindowMsFromOd(od) / rate);
	}

	// osu!std approach-rate <-> preempt-time formula (osu! wiki "Approach rate").
	private static double PreemptMsFromAr(double ar)
	{
		return ar <= 5 ? 1800 - 120 * ar : 1200 - 150 * (ar - 5);
	}

	private static double ArFromPreemptMs(double preemptMs)
	{
		return preemptMs >= 1200 ? (1800 - preemptMs) / 120 : 5 + (1200 - preemptMs) / 150;
	}

	// osu!std overall-difficulty <-> "300" hit-window formula (osu! wiki "Overall difficulty").
	private static double HitWindowMsFromOd(double od)
	{
		return 80 - 6 * od;
	}

	private static double OdFromHitWindowMs(double windowMs)
	{
		return (80 - windowMs) / 6;
	}
}
