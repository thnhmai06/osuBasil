using Basil.Application.Beatmaps.Persistence;

namespace Basil.Application.Beatmaps.Ports;

internal interface IBeatmapsetScanner
{
	Task<BeatmapsetScanOutcome> ScanAsync(CancellationToken ct = default);

	Task<BeatmapsetScanOutcome> ScanAsync(BeatmapsetSource entry, CancellationToken ct = default);
}