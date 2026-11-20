using Basil.Application.Beatmaps.Persistence;
using Basil.Domain.Beatmaps;

namespace Basil.Infrastructure.Beatmaps.Logic;

internal sealed record BeatmapsetPlan(
	BeatmapsetArchive Archive,
	BeatmapsetRecord? Existing,
	BeatmapsetData Data,
	bool Locked)
{
	public BeatmapsetChange Change => new(Existing, Data);
}