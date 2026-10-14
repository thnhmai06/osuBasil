using Basil.Domain.Beatmaps;

namespace Basil.Application.Services.Contracts.Beatmaps;

/// <summary>The reasons a beatmapset cannot be imported.</summary>
public enum BeatmapsetImportFailure : byte
{
	/// <summary>The content is not a readable beatmapset.</summary>
	Unreadable,

	/// <summary>The beatmapset it belongs to is locked.</summary>
	Locked,

	/// <summary>An earlier beatmapset of the same upload is the same beatmapset or shares a difficulty with it.</summary>
	Duplicate
}

/// <summary>The outcome of importing one beatmapset found in an upload.</summary>
/// <param name="Name">The path within the upload of the archive or folder the beatmapset was found as.</param>
/// <param name="Set">The imported beatmapset, or <see langword="null" /> on failure.</param>
/// <param name="Beatmaps">The imported beatmaps; empty on failure.</param>
/// <param name="Failure">Why the import failed, or <see langword="null" /> on success.</param>
public sealed record BeatmapsetImportResult(
	string Name,
	Beatmapset? Set,
	IReadOnlyList<Beatmap> Beatmaps,
	BeatmapsetImportFailure? Failure);
