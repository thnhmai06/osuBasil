using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Common.Common;
using Basil.Domain.Beatmaps;
using Basil.Domain.Content;

namespace Basil.Application.Beatmaps.Capabilities;

/// <summary>Keeps the beatmapsets the server has in step with the archives in the storage.</summary>
internal sealed class BeatmapsetScanner(
	IBeatmapsetStorage storage,
	IBeatmapsetRepository beatmapsets,
	IBeatmapRepository beatmaps,
	BeatmapsetPlanner planner,
	BeatmapsetStorer storer,
	BeatmapsetFileWriter files,
	BeatmapsetFileAudit audit)
{
	/// <summary>Brings the server in step with every entry of the storage.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	public async Task<BeatmapsetScanOutcome> ScanAsync(CancellationToken cancellationToken = default)
	{
		var packed = new List<BeatmapsetArchive>();
		try
		{
			return await ScanAllAsync(packed, cancellationToken);
		}
		finally
		{
			// What was taken in has moved into a set's folder; the rest is not kept.
			foreach (var archive in packed)
				await storage.DeleteAsync(archive, CancellationToken.None);
		}
	}

	/// <summary>Brings the server in step with one entry of the storage.</summary>
	/// <param name="entry">The entry as it is now.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	public async Task<BeatmapsetScanOutcome> ScanAsync(BeatmapsetSource entry,
		CancellationToken cancellationToken = default)
	{
		return entry switch
		{
			BeatmapsetArchive archive => await ScanArchiveAsync(archive, null, cancellationToken),
			BeatmapsetFolder folder => await ScanFolderAsync(folder, cancellationToken),
			_ => BeatmapsetScanOutcome.NoChange
		};
	}

	/// <summary>Brings the server in step with every entry of the storage.</summary>
	/// <param name="packed">The archives packed from loose files during the scan, added to as they are packed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	private async Task<BeatmapsetScanOutcome> ScanAllAsync(
		List<BeatmapsetArchive> packed, CancellationToken cancellationToken)
	{
		var claims = new SetClaims();
		var entries = await ClassifyAsync(packed, claims, cancellationToken);
		var indexed = await PlanQueuedAsync(entries, claims, cancellationToken);
		var (remove, storedSets) = await CommitAsync(entries, indexed, cancellationToken);
		var imported = await StoreFilesAsync(entries, indexed, storedSets, cancellationToken);

		return new BeatmapsetScanOutcome(
			new BeatmapsetScanResult(
				indexed.Count(item => item.Existing is null),
				indexed.Count(item => item.Existing is not null),
				remove.Count),
			imported,
			remove);
	}

	/// <summary>Sorts every entry of the storage into what is queued for reading, unchanged and changed in name only.</summary>
	/// <param name="packed">The archives packed from loose files, added to as they are packed.</param>
	/// <param name="claims">The claims of this scan; a changed set claims its place.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The entries of the scan.</returns>
	private async Task<ScanEntries> ClassifyAsync(List<BeatmapsetArchive> packed, SetClaims claims,
		CancellationToken cancellationToken)
	{
		var entries = new ScanEntries();
		var seen = entries.Seen;
		var queued = entries.Queued;
		var unchanged = entries.Unchanged;
		var changed = entries.Changed;

		foreach (var entry in await storage.ListAsync(cancellationToken))
			switch (entry)
			{
				case BeatmapsetArchive loose:
					queued.Add((loose, null, null));
					break;

				case BeatmapsetFolder { Exists: true, Archive: null } folder:
				{
					var packedArchive = await storage.PackAsync(folder, cancellationToken);
					if (packedArchive is null) break;

					packed.Add(packedArchive);
					queued.Add((packedArchive, folder.Id, null));
					break;
				}

				case BeatmapsetFolder { Exists: true, Archive: { } archive } folder:
				{
					BeatmapsetRecord? stored = null;
					if (folder.Id is { } id)
						stored = await beatmapsets.GetAsync(id, cancellationToken);

					if (stored is not null && stored.Fingerprint.Stamp == archive.Stamp)
					{
						seen.Add(stored.Id);
						unchanged.Add(stored);
						break;
					}

					var fingerprint = await archive.FingerprintAsync(cancellationToken);
					if (stored is not null && stored.Fingerprint.Hash == fingerprint.Hash)
					{
						seen.Add(stored.Id);
						var change = await planner.ChangeOfAsync(stored, fingerprint, cancellationToken);
						claims.TryClaim(change);
						changed.Add((change, stored));
						break;
					}

					queued.Add((archive, folder.Id, fingerprint));
					break;
				}
			}

		return entries;
	}

	/// <summary>
	///     Reads and plans the queued archives; those that a set of this scan already has, or that are locked, are left
	///     out.
	/// </summary>
	/// <param name="entries">The entries of the scan; the sets it finds are added to <see cref="ScanEntries.Seen" />.</param>
	/// <param name="claims">The claims of this scan.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The planned archives that are to be stored.</returns>
	private async Task<List<BeatmapsetPlan>> PlanQueuedAsync(ScanEntries entries, SetClaims claims,
		CancellationToken cancellationToken)
	{
		var seen = entries.Seen;
		var indexed = new List<BeatmapsetPlan>();

		foreach (var (archive, folderId, fingerprint) in entries.Queued)
		{
			// An archive that cannot be read keeps its files; the set of the folder it lay in is not seen and is forgotten.
			var (content, stream) = await planner.ReadAsync(archive, cancellationToken);
			if (content is null) continue;

			await using (stream!)
			await using (content)
			{
				var item = await planner.PlanAsync(archive, fingerprint, content, folderId, cancellationToken);
				if (item.Locked)
				{
					if (item.Existing is not null) seen.Add(item.Existing.Id);
					continue;
				}

				if (item.Existing is not null) seen.Add(item.Existing.Id);
				// Another archive of this scan already becomes that set; this one waits for the next scan.
				if (!claims.TryClaim(item.Change)) continue;

				indexed.Add(item);
			}
		}

		return indexed;
	}

	/// <summary>Forgets the sets the scan did not see and commits every change of the scan at once.</summary>
	/// <param name="entries">The entries of the scan.</param>
	/// <param name="indexed">The planned archives that are to be stored.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The forgotten sets and the stored sets: first those of the planned archives, then the changed ones.</returns>
	private async Task<(List<BeatmapsetRecord> Remove, IReadOnlyList<BeatmapsetRecord> StoredSets)> CommitAsync(
		ScanEntries entries, List<BeatmapsetPlan> indexed, CancellationToken cancellationToken)
	{
		var remove = (
				await Paging.ListAllAsync(page => beatmapsets.ListAsync(
					new BeatmapQuery(), page, cancellationToken), cancellationToken))
			.Where(set => !entries.Seen.Contains(set.Id))
			.ToList();

		var store = indexed.Select(x => x.Change)
			.Concat(entries.Changed.Select(x => x.Change))
			.ToList();

		var storedSets =
			await beatmapsets.CommitAsync(new BeatmapsetChanges(store, remove), cancellationToken);

		return (remove, storedSets);
	}

	/// <summary>Saves the archives and makes the files of what was stored, and makes missing files again.</summary>
	/// <param name="entries">The entries of the scan.</param>
	/// <param name="indexed">The planned archives that were stored.</param>
	/// <param name="storedSets">The stored sets: first those of the planned archives, then the changed ones.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The beatmapsets taken in, with their difficulties.</returns>
	private async Task<List<BeatmapsetIndexed>> StoreFilesAsync(ScanEntries entries, List<BeatmapsetPlan> indexed,
		IReadOnlyList<BeatmapsetRecord> storedSets, CancellationToken cancellationToken)
	{
		var imported = new List<BeatmapsetIndexed>();

		for (var i = 0; i < indexed.Count; i++)
		{
			var item = indexed[i];
			var set = storedSets[i];
			await storage.SaveAsync(set, item.Archive, cancellationToken);
			await files.StoreFilesAsync(set, cancellationToken);
			var storedBeatmaps = await beatmaps.ListAsync(set, cancellationToken);
			imported.Add(new BeatmapsetIndexed(set, storedBeatmaps));
		}

		for (var i = 0; i < entries.Changed.Count; i++)
		{
			var set = storedSets[indexed.Count + i];
			if (await audit.MissingFilesAsync(set, cancellationToken))
				await files.StoreFilesAsync(set, cancellationToken);
		}

		foreach (var set in entries.Unchanged)
			if (await audit.MissingFilesAsync(set, cancellationToken))
				await files.StoreFilesAsync(set, cancellationToken);

		return imported;
	}

	/// <summary>Brings the server in step with one folder of the storage.</summary>
	/// <param name="folder">The folder as it is now.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	/// <remarks>The folder keeps its files when its beatmapset is forgotten.</remarks>
	private async Task<BeatmapsetScanOutcome> ScanFolderAsync(BeatmapsetFolder folder,
		CancellationToken cancellationToken)
	{
		if (folder is { Exists: true, Archive: { } archive })
			return await ScanArchiveAsync(archive, folder.Id, cancellationToken);

		// A folder that holds no archive holds loose files, which are packed before they are read; a folder that is
		// gone packs to nothing.
		var packed = await storage.PackAsync(folder, cancellationToken);
		if (packed is not null)
			try
			{
				return await ScanArchiveAsync(packed, folder.Id, cancellationToken);
			}
			finally
			{
				await storage.DeleteAsync(packed, CancellationToken.None);
			}

		// The folder or its archive is gone: the beatmapset it held is forgotten.
		if (folder.Id is not { } id) return BeatmapsetScanOutcome.NoChange;

		var held = await beatmapsets.GetAsync(id, cancellationToken);
		if (held is null) return BeatmapsetScanOutcome.NoChange;

		// The set may still have its archive in another folder (the server renames folders itself).
		var remaining = await storage.OpenArchiveAsync(held, true, cancellationToken);
		if (remaining is not null)
		{
			await remaining.DisposeAsync();
			return BeatmapsetScanOutcome.NoChange;
		}

		var deleted = await storer.ForgetAsync(held, cancellationToken);
		return BeatmapsetScanOutcome.Forgotten(deleted);
	}

	/// <summary>Brings the server in step with one archive lying in the storage.</summary>
	/// <param name="archive">The archive as it is now.</param>
	/// <param name="folderId">The id of the folder the archive lies in, or <see langword="null" /> when it lies loose.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	/// <remarks>
	///     An archive whose hash is the one stored for the folder's beatmapset needs no indexing; an archive that cannot
	///     be read forgets the beatmapset of the folder it lay in, keeping its files.
	/// </remarks>
	private async Task<BeatmapsetScanOutcome> ScanArchiveAsync(BeatmapsetArchive archive, int? folderId,
		CancellationToken cancellationToken)
	{
		var fingerprint = await archive.FingerprintAsync(cancellationToken);

		BeatmapsetRecord? stored = null;
		if (folderId is { } id)
			stored = await beatmapsets.GetAsync(id, cancellationToken);

		// Unchanged. Missing files are not looked for here: the server's own writes into a set's folder come back as
		// changes while an index may still be writing; the full scan makes missing files again.
		if (stored is not null && stored.Fingerprint.Hash == fingerprint.Hash) return BeatmapsetScanOutcome.NoChange;

		var (content, stream) = await planner.ReadAsync(archive, cancellationToken);
		if (content is null)
		{
			// The archive cannot be read: the set of the folder it lay in is forgotten, its files kept as they are.
			if (stored is null) return BeatmapsetScanOutcome.NoChange;

			var deleted = await storer.ForgetAsync(stored, cancellationToken);
			return BeatmapsetScanOutcome.Forgotten(deleted);
		}

		BeatmapsetPlan item;
		await using (stream!)
		await using (content)
		{
			item = await planner.PlanAsync(archive, fingerprint, content, folderId, cancellationToken);
		}

		if (item.Locked) return BeatmapsetScanOutcome.NoChange;

		// The archive is closed first: it is moved into the set's folder.
		var imported = await storer.StoreAsync(item, cancellationToken);
		return new BeatmapsetScanOutcome(
			new BeatmapsetScanResult(item.Existing is null ? 1 : 0, item.Existing is null ? 0 : 1, 0),
			[imported], []);
	}

	/// <summary>The entries of the storage as one scan sorted them.</summary>
	private sealed class ScanEntries
	{
		/// <summary>The ids of the stored sets the scan found their archive for.</summary>
		public HashSet<int> Seen { get; } = [];

		/// <summary>The archives to read, with the id of their folder and their fingerprint when it is already taken.</summary>
		public List<(BeatmapsetArchive Archive, int? FolderId, ContentFingerprint? Fingerprint)> Queued { get; } = [];

		/// <summary>The sets whose archive has not changed.</summary>
		public List<BeatmapsetRecord> Unchanged { get; } = [];

		/// <summary>The sets whose archive changed in name only, with the change that stores them.</summary>
		public List<(BeatmapsetChange Change, BeatmapsetRecord Set)> Changed { get; } = [];
	}
}