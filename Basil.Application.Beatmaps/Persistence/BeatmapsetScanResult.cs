using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Persistence;

public sealed record BeatmapsetScanResult(int Imported, int Updated, int Forgotten);

public sealed record BeatmapsetScanOutcome(
	BeatmapsetScanResult Result,
	IReadOnlyList<BeatmapsetIndexed> Imported,
	IReadOnlyList<BeatmapsetRecord> Deleted)
{
	public static BeatmapsetScanOutcome NoChange { get; } = new(new BeatmapsetScanResult(0, 0, 0), [], []);

	public static BeatmapsetScanOutcome Forgotten(BeatmapsetRecord set)
	{
		return new BeatmapsetScanOutcome(new BeatmapsetScanResult(0, 0, 1), [], [set]);
	}
}

public sealed record BeatmapsetIndexed(BeatmapsetRecord Set, IReadOnlyList<BeatmapRecord> Beatmaps);