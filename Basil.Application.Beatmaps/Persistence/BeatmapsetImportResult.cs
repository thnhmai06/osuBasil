using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Persistence;

public sealed record BeatmapsetImportResult(
	BeatmapsetRecord? Set,
	IReadOnlyList<BeatmapRecord> Beatmaps,
	BeatmapsetImportFailure? Failure);

public enum BeatmapsetImportFailure : byte
{
	Unreadable,

	Locked,

	AlreadyExists,

	/// <summary>The osu! calculation stack is not installed, so no beatmapset can be read.</summary>
	RulesetsMissing
}