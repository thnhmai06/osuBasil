using Basil.Domain.Beatmaps;

namespace Basil.Application.Persistence.Beatmaps;

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
	IReadOnlyList<BeatmapsetRecord> Deleted)
{
	/// <summary>Gets the outcome of a scan that changed nothing.</summary>
	public static BeatmapsetScanOutcome NoChange { get; } = new(new BeatmapsetScanResult(0, 0, 0), [], []);

	/// <summary>Gets the outcome of a scan that forgot one beatmapset.</summary>
	/// <param name="set">The forgotten beatmapset.</param>
	/// <returns>The outcome, with <paramref name="set" /> as the only deleted beatmapset.</returns>
	public static BeatmapsetScanOutcome Forgotten(BeatmapsetRecord set)
	{
		return new BeatmapsetScanOutcome(new BeatmapsetScanResult(0, 0, 1), [], [set]);
	}
}

/// <summary>A beatmapset and its difficulties after indexing.</summary>
/// <param name="Set">The beatmapset that was indexed.</param>
/// <param name="Beatmaps">Its difficulties after indexing.</param>
public sealed record BeatmapsetIndexed(BeatmapsetRecord Set, IReadOnlyList<BeatmapRecord> Beatmaps);