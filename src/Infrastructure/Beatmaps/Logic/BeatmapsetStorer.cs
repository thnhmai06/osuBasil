using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;

namespace Basil.Infrastructure.Beatmaps.Logic;

/// <summary>Stores planned beatmapsets and forgets stored ones.</summary>
internal sealed class BeatmapsetStorer(
	IBeatmapsetRepository beatmapsets,
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage,
	BeatmapsetFileWriter files)
{
	/// <summary>Stores one planned beatmapset and makes every file it serves.</summary>
	/// <param name="item">The archive, as planned from what it contains.</param>
	/// <returns>The stored beatmapset with its difficulties.</returns>
	public async Task<BeatmapsetIndexed> StoreAsync(BeatmapsetPlan item)
	{
		var storedSets = await beatmapsets.CommitAsync(new BeatmapsetChanges([item.Change], []));
		var set = storedSets[0];

		await storage.SaveAsync(set, item.Archive);
		await files.StoreFilesAsync(set);

		var storedBeatmaps = await beatmaps.ListAsync(set);
		return new BeatmapsetIndexed(set, storedBeatmaps);
	}

	/// <summary>Forgets a stored beatmapset.</summary>
	/// <param name="set">The beatmapset to forget.</param>
	/// <returns>The forgotten beatmapset.</returns>
	public async Task<BeatmapsetRecord> ForgetAsync(BeatmapsetRecord set)
	{
		await beatmapsets.CommitAsync(new BeatmapsetChanges([], [set]));
		return set;
	}
}