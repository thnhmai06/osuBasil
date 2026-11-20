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

	AlreadyExists
}