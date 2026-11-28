using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Ports;

public interface IBeatmapAssets
{
	Task BuildAsync(BeatmapsetRecord set, IReadOnlyList<BeatmapRecord> beatmaps);
}