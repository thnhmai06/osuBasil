using Basil.Domain.Beatmaps;

namespace Basil.Application.Contracts.Beatmaps;

/// <summary>The reasons a beatmapset archive cannot be imported.</summary>
public enum BeatmapsetImportFailure : byte
{
	/// <summary>The content is not a readable beatmapset archive.</summary>
	Unreadable,

	/// <summary>The beatmapset it belongs to is locked.</summary>
	Locked
}

/// <summary>The outcome of importing a beatmapset archive.</summary>
/// <param name="Set">The imported beatmapset, or <see langword="null" /> on failure.</param>
/// <param name="Beatmaps">The imported beatmaps; empty on failure.</param>
/// <param name="Failure">Why the import failed, or <see langword="null" /> on success.</param>
public sealed record BeatmapsetImportResult(
	Beatmapset? Set,
	IReadOnlyList<Beatmap> Beatmaps,
	BeatmapsetImportFailure? Failure);