using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Storage;
using Basil.Domain.Beatmaps;
using Basil.Domain.Content;

namespace Basil.Infrastructure.Beatmaps.Logic;

internal sealed class BeatmapsetScanner(
	IBeatmapsetStorage storage,
	IBeatmapsetRepository beatmapsets,
	IBeatmapRepository beatmaps,
	BeatmapsetPlanner planner,
	BeatmapsetStorer storer,
	BeatmapsetFileWriter files,
	BeatmapsetFileAudit audit) : IBeatmapsetScanner
{
	public async Task<BeatmapsetScanOutcome> ScanAsync(CancellationToken ct = default)
	{
		var packed = new List<BeatmapsetArchive>();
		try
		{
			return await ScanAllAsync(packed, ct);
		}
		finally
		{
			// What was taken in has moved into a set's folder; the rest is not kept.
			foreach (var archive in packed)
				await storage.DeleteAsync(archive);
		}
	}

	public async Task<BeatmapsetScanOutcome> ScanAsync(BeatmapsetSource entry,
		CancellationToken ct = default)
	{
		return entry switch
		{
			BeatmapsetArchive archive => await ScanArchiveAsync(archive, null, ct),
			BeatmapsetFolder folder => await ScanFolderAsync(folder, ct),
			_ => BeatmapsetScanOutcome.NoChange
		};
	}

	private async Task<BeatmapsetScanOutcome> ScanAllAsync(
		List<BeatmapsetArchive> packed, CancellationToken ct)
	{
		var claims = new SetClaims();
		var entries = await ClassifyAsync(packed, claims, ct);
		var indexed = await PlanQueuedAsync(entries, claims, ct);
		var (remove, storedSets) = await CommitAsync(entries, indexed);
		var imported = await StoreFilesAsync(entries, indexed, storedSets);

		return new BeatmapsetScanOutcome(
			new BeatmapsetScanResult(
				indexed.Count(item => item.Existing is null),
				indexed.Count(item => item.Existing is not null),
				remove.Count),
			imported,
			remove);
	}

	private async Task<ScanEntries> ClassifyAsync(List<BeatmapsetArchive> packed, SetClaims claims,
		CancellationToken ct)
	{
		var entries = new ScanEntries();
		var seen = entries.Seen;
		var queued = entries.Queued;
		var unchanged = entries.Unchanged;
		var changed = entries.Changed;

		foreach (var entry in await storage.ListAsync(ct))
			switch (entry)
			{
				case BeatmapsetArchive loose:
					queued.Add((loose, null, null));
					break;

				case BeatmapsetFolder { Exists: true, Archive: null } folder:
				{
					var packedArchive = await storage.PackAsync(folder, ct);
					if (packedArchive is null) break;

					packed.Add(packedArchive);
					queued.Add((packedArchive, folder.Id, null));
					break;
				}

				case BeatmapsetFolder { Exists: true, Archive: { } archive } folder:
				{
					BeatmapsetRecord? stored = null;
					if (folder.Id is { } id)
						stored = await beatmapsets.GetAsync(id, ct);

					if (stored is not null && stored.Fingerprint.Stamp == archive.Stamp)
					{
						seen.Add(stored.Id);
						unchanged.Add(stored);
						break;
					}

					var fingerprint = await archive.FingerprintAsync(ct);
					if (stored is not null && stored.Fingerprint.Hash == fingerprint.Hash)
					{
						seen.Add(stored.Id);
						var change = await planner.ChangeOfAsync(stored, fingerprint, ct);
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

	private async Task<List<BeatmapsetPlan>> PlanQueuedAsync(ScanEntries entries, SetClaims claims,
		CancellationToken ct)
	{
		var seen = entries.Seen;
		var indexed = new List<BeatmapsetPlan>();

		foreach (var (archive, folderId, fingerprint) in entries.Queued)
		{
			// An archive that cannot be read keeps its files; the set of the folder it lay in is not seen and is forgotten.
			var (content, stream) = await planner.ReadAsync(archive, ct);
			if (content is null) continue;

			await using (stream!)
			await using (content)
			{
				var item = await planner.PlanAsync(archive, fingerprint, content, folderId, ct);
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

	private async Task<(List<BeatmapsetRecord> Remove, IReadOnlyList<BeatmapsetRecord> StoredSets)> CommitAsync(
		ScanEntries entries, List<BeatmapsetPlan> indexed)
	{
		var remove = new List<BeatmapsetRecord>();
		foreach (var id in (await beatmapsets.ListIdsAsync()).Where(id => !entries.Seen.Contains(id)))
			if (await beatmapsets.GetAsync(id) is { } forgotten)
				remove.Add(forgotten);

		var store = indexed.Select(x => x.Change)
			.Concat(entries.Changed.Select(x => x.Change))
			.ToList();

		var storedSets =
			await beatmapsets.CommitAsync(new BeatmapsetChanges(store, remove));

		return (remove, storedSets);
	}

	private async Task<List<BeatmapsetIndexed>> StoreFilesAsync(ScanEntries entries, List<BeatmapsetPlan> indexed,
		IReadOnlyList<BeatmapsetRecord> storedSets)
	{
		var imported = new List<BeatmapsetIndexed>();

		for (var i = 0; i < indexed.Count; i++)
		{
			var item = indexed[i];
			var set = storedSets[i];
			await storage.SaveAsync(set, item.Archive);
			await files.StoreFilesAsync(set);
			var storedBeatmaps = await beatmaps.ListAsync(set);
			imported.Add(new BeatmapsetIndexed(set, storedBeatmaps));
		}

		for (var i = 0; i < entries.Changed.Count; i++)
		{
			var set = storedSets[indexed.Count + i];
			if (await audit.MissingFilesAsync(set))
				await files.StoreFilesAsync(set);
		}

		foreach (var set in entries.Unchanged)
			if (await audit.MissingFilesAsync(set))
				await files.StoreFilesAsync(set);

		return imported;
	}

	private async Task<BeatmapsetScanOutcome> ScanFolderAsync(BeatmapsetFolder folder,
		CancellationToken ct)
	{
		if (folder is { Exists: true, Archive: { } archive })
			return await ScanArchiveAsync(archive, folder.Id, ct);

		// A folder that holds no archive holds loose files, which are packed before they are read; a folder that is
		// gone packs to nothing.
		var packed = await storage.PackAsync(folder, ct);
		if (packed is not null)
			try
			{
				return await ScanArchiveAsync(packed, folder.Id, ct);
			}
			finally
			{
				await storage.DeleteAsync(packed);
			}

		// The folder or its archive is gone: the beatmapset it held is forgotten.
		if (folder.Id is not { } id) return BeatmapsetScanOutcome.NoChange;

		var held = await beatmapsets.GetAsync(id, ct);
		if (held is null) return BeatmapsetScanOutcome.NoChange;

		// The set may still have its archive in another folder (the server renames folders itself).
		var remaining = await storage.OpenArchiveAsync(held, true, ct);
		if (remaining is not null)
		{
			await remaining.DisposeAsync();
			return BeatmapsetScanOutcome.NoChange;
		}

		var deleted = await storer.ForgetAsync(held);
		return BeatmapsetScanOutcome.Forgotten(deleted);
	}

	private async Task<BeatmapsetScanOutcome> ScanArchiveAsync(BeatmapsetArchive archive, int? folderId,
		CancellationToken ct)
	{
		var fingerprint = await archive.FingerprintAsync(ct);

		BeatmapsetRecord? stored = null;
		if (folderId is { } id)
			stored = await beatmapsets.GetAsync(id, ct);

		// Unchanged. Missing files are not looked for here: the server's own writes into a set's folder come back as
		// changes while an index may still be writing; the full scan makes missing files again.
		if (stored is not null && stored.Fingerprint.Hash == fingerprint.Hash) return BeatmapsetScanOutcome.NoChange;

		var (content, stream) = await planner.ReadAsync(archive, ct);
		if (content is null)
		{
			// The archive cannot be read: the set of the folder it lay in is forgotten, its files kept as they are.
			if (stored is null) return BeatmapsetScanOutcome.NoChange;

			var deleted = await storer.ForgetAsync(stored);
			return BeatmapsetScanOutcome.Forgotten(deleted);
		}

		BeatmapsetPlan item;
		await using (stream!)
		await using (content)
		{
			item = await planner.PlanAsync(archive, fingerprint, content, folderId, ct);
		}

		if (item.Locked) return BeatmapsetScanOutcome.NoChange;

		// The archive is closed first: it is moved into the set's folder.
		var imported = await storer.StoreAsync(item);
		return new BeatmapsetScanOutcome(
			new BeatmapsetScanResult(item.Existing is null ? 1 : 0, item.Existing is null ? 0 : 1, 0),
			[imported], []);
	}

	private sealed class ScanEntries
	{
		public HashSet<int> Seen { get; } = [];

		public List<(BeatmapsetArchive Archive, int? FolderId, ContentFingerprint? Fingerprint)> Queued { get; } = [];

		public List<BeatmapsetRecord> Unchanged { get; } = [];

		public List<(BeatmapsetChange Change, BeatmapsetRecord Set)> Changed { get; } = [];
	}
}