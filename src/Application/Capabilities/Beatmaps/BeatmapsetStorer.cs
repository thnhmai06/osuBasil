using Basil.Application.Persistence.Beatmaps;
using Basil.Domain.Beatmaps;

namespace Basil.Application.Capabilities.Beatmaps;

/// <summary>Stores planned beatmapsets and forgets stored ones.</summary>
internal sealed class BeatmapsetStorer(
	IBeatmapsetRepository beatmapsets,
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage,
	BeatmapsetFileWriter files)
{
	/// <summary>Stores one planned beatmapset and makes every file it serves.</summary>
	/// <param name="item">The archive, as planned from what it contains.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored beatmapset with its difficulties.</returns>
	public async Task<BeatmapsetIndexed> StoreAsync(BeatmapsetPlan item,
		CancellationToken cancellationToken)
	{
		var storedSets = await beatmapsets.CommitAsync(new BeatmapsetChanges([item.Change], []), cancellationToken);
		var set = storedSets[0];

		await storage.SaveAsync(set, item.Archive, cancellationToken);
		await files.StoreFilesAsync(set, cancellationToken);

		var storedBeatmaps = await beatmaps.ListAsync(set, cancellationToken);
		return new BeatmapsetIndexed(set, storedBeatmaps);
	}

	/// <summary>Forgets a stored beatmapset.</summary>
	/// <param name="set">The beatmapset to forget.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The forgotten beatmapset.</returns>
	public async Task<BeatmapsetRecord> ForgetAsync(BeatmapsetRecord set, CancellationToken cancellationToken)
	{
		await beatmapsets.CommitAsync(new BeatmapsetChanges([], [set]), cancellationToken);
		return set;
	}
}