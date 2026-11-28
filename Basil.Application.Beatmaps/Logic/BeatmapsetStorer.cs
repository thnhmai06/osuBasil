using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Beatmaps.Logic;

internal sealed class BeatmapsetStorer(
	IBeatmapsetRepository beatmapsets,
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage,
	BeatmapsetFileWriter files)
{
	public async Task<BeatmapsetIndexed> StoreAsync(BeatmapsetPlan item)
	{
		var storedSets = await beatmapsets.CommitAsync(new BeatmapsetChanges([item.Change], []));
		var set = storedSets[0];

		await storage.SaveAsync(set, item.Archive);
		await files.StoreFilesAsync(set);

		var storedBeatmaps = await beatmaps.ListAsync(set);
		return new BeatmapsetIndexed(set, storedBeatmaps);
	}

	public async Task<BeatmapsetRecord> ForgetAsync(BeatmapsetRecord set)
	{
		await beatmapsets.CommitAsync(new BeatmapsetChanges([], [set]));
		return set;
	}
}