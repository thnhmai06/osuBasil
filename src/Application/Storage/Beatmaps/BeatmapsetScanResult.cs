using Basil.Domain.Beatmaps;

namespace Basil.Application.Storage.Beatmaps;

/// <summary>How many beatmapsets a scan changed.</summary>
/// <param name="Imported">The beatmapsets taken in for the first time.</param>
/// <param name="Updated">The beatmapsets whose difficulties changed.</param>
/// <param name="Forgotten">The beatmapsets forgotten because nothing is stored for them.</param>
public sealed record BeatmapsetScanResult(int Imported, int Updated, int Forgotten);

/// <summary>The beatmapsets taken in or forgotten by a scan, in the order each change happened.</summary>
/// <param name="Result">The counts of beatmapsets changed by the scan.</param>
/// <param name="Imported">The beatmapsets imported or re-imported, in the order they were taken in.</param>
/// <param name="Deleted">The beatmapsets forgotten, in the order they were removed.</param>
public sealed record BeatmapsetScanOutcome(
	BeatmapsetScanResult Result,
	IReadOnlyList<BeatmapsetIndexed> Imported,
	IReadOnlyList<BeatmapsetRecord> Deleted);

/// <summary>A beatmapset and its difficulties after indexing.</summary>
/// <param name="Set">The beatmapset that was indexed.</param>
/// <param name="Beatmaps">Its difficulties after indexing.</param>
public sealed record BeatmapsetIndexed(BeatmapsetRecord Set, IReadOnlyList<BeatmapRecord> Beatmaps);
