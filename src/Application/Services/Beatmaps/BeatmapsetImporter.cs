using Basil.Application.Storage.Beatmaps;

namespace Basil.Application.Services.Beatmaps;

/// <summary>Imports one uploaded beatmapset archive.</summary>
internal sealed class BeatmapsetImporter(
	IBeatmapsetStorage storage,
	BeatmapsetPlanner planner,
	BeatmapsetStorer storer)
{
	/// <summary>Imports one uploaded archive as a beatmapset.</summary>
	/// <param name="archive">The uploaded archive.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The result of the import and the beatmapset taken in, or <see langword="null" /> when it was rejected.</returns>
	public async Task<(BeatmapsetImportResult Result, BeatmapsetIndexed? Imported)> ImportAsync(Stream archive,
		CancellationToken cancellationToken = default)
	{
		var uploaded = await storage.SaveAsync(archive, cancellationToken).ConfigureAwait(false);
		try
		{
			var (content, stream) = await planner.ReadAsync(uploaded, cancellationToken).ConfigureAwait(false);
			if (content is null)
				return (new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Unreadable), null);

			BeatmapsetPlan item;
			await using (stream!.ConfigureAwait(false))
			await using (content.ConfigureAwait(false))
			{
				item = await planner.PlanAsync(uploaded, null, content, null, cancellationToken).ConfigureAwait(false);
			}

			if (item.Locked)
				return (new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Locked), null);

			// A set whose difficulties repeat one another cannot be stored.
			if (!new SetClaims().TryClaim(item.Change))
				return (new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Unreadable), null);

			// The archive is closed first: it is moved into the set's folder.
			var imported = await storer.StoreAsync(item, cancellationToken).ConfigureAwait(false);
			return (new BeatmapsetImportResult(imported.Set, imported.Beatmaps, null), imported);
		}
		finally
		{
			// Taken in, the archive has moved into its set's folder; rejected, nothing of it is kept.
			await storage.DeleteAsync(uploaded, CancellationToken.None).ConfigureAwait(false);
		}
	}
}