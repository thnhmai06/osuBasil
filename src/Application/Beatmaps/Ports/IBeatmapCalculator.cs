using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Application.Beatmaps.Ports;

public interface IBeatmapCalculator
{
	Task<double> GetStarsAsync(BeatmapRecord beatmap, GameMode mode, GameMods mods, CancellationToken ct = default);

	Task<BeatmapPerformance> GetPerformanceAsync(BeatmapRecord beatmap, GameMode mode, GameMods mods, HitCounts hits,
		int combo, CancellationToken ct = default);
}

public readonly record struct BeatmapPerformance(double Pp, double MaxPp);
