using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Persistence;

public sealed record BeatmapsetChange(BeatmapsetRecord? Existing, BeatmapsetData Data);

public sealed record BeatmapsetChanges(
	IReadOnlyList<BeatmapsetChange> Store,
	IReadOnlyCollection<BeatmapsetRecord> Remove);