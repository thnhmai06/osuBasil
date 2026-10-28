using Basil.Application.Beatmaps.Persistence;

namespace Basil.Application.Beatmaps.Capabilities;

/// <summary>Imports one uploaded beatmapset archive.</summary>
internal sealed class BeatmapsetImporter(
	IBeatmapsetStorage storage,
	BeatmapsetPlanner planner,
	BeatmapsetStorer storer)
{
	/// <summary>Imports one uploaded archive as a beatmapset.</summary>
	/// <param name="archive">The uploaded archive.</param>
	/// <param name="replaceExisting">
	///     Whether an archive of a beatmapset the server already has replaces it; otherwise it is rejected.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The result of the import and the beatmapset taken in, or <see langword="null" /> when it was rejected.</returns>
	public async Task<(BeatmapsetImportResult Result, BeatmapsetIndexed? Imported)> ImportAsync(Stream archive,
		bool replaceExisting, CancellationToken cancellationToken = default)
	{
		var uploaded = await storage.SaveAsync(archive, cancellationToken);
		try
		{
			var (content, stream) = await planner.ReadAsync(uploaded, cancellationToken);
			if (content is null)
				return (new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Unreadable), null);

			BeatmapsetPlan item;
			await using (stream!)
			await using (content)
			{
				item = await planner.PlanAsync(uploaded, null, content, null, cancellationToken);
			}

			if (item.Locked)
				return (new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Locked), null);

			if (!replaceExisting && item.Existing is not null)
				return (new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.AlreadyExists), null);

			// A set whose difficulties repeat one another cannot be stored.
			if (!new SetClaims().TryClaim(item.Change))
				return (new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Unreadable), null);

			// The archive is closed first: it is moved into the set's folder.
			var imported = await storer.StoreAsync(item, cancellationToken);
			return (new BeatmapsetImportResult(imported.Set, imported.Beatmaps, null), imported);
		}
		finally
		{
			// Taken in, the archive has moved into its set's folder; rejected, nothing of it is kept.
			await storage.DeleteAsync(uploaded, CancellationToken.None);
		}
	}
}