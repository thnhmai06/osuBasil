using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Application.Services.Implementations.Common;
using Basil.Application.Storage.Contracts.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Utilities;

namespace Basil.Application.Services.Implementations.Beatmaps;

/// <summary>
///     Imports, deletes and keeps track of the beatmapsets the server has.
/// </summary>
internal sealed class BeatmapsetService(
	IBeatmapsetReader reader,
	IBeatmapAnalyser analyser,
	IBeatmapAssets assets,
	IBeatmapsetRepository beatmapsets,
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage) : IBeatmapsetService
{
	/// <summary>The name passed when a storyboard file is stored or looked for; its kind decides where it lies.</summary>
	private const string StoryboardFileName = "storyboard.osb";

	/// <summary>A scan that changed nothing.</summary>
	private static readonly BeatmapsetScanResult NoChange = new(0, 0, 0);

	/// <summary>The names Windows keeps for devices, whatever their extension.</summary>
	private static readonly Regex ReservedName =
		new(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[0-9¹²³]|LPT[0-9¹²³]) *$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private readonly Channel<BeatmapsetEvent> _events = Channel.CreateUnbounded<BeatmapsetEvent>();

	/// <inheritdoc />
	public ChannelReader<BeatmapsetEvent> Events => _events.Reader;

	/// <inheritdoc />
	public async Task<IReadOnlyList<BeatmapsetImportResult>> ImportAsync(IReadOnlyList<BeatmapsetUploadFile> files,
		CancellationToken cancellationToken = default)
	{
		var upload = await storage.SaveAsync(files, cancellationToken).ConfigureAwait(false);
		try
		{
			var results = new BeatmapsetImportResult?[upload.Archives.Count];
			var accepted = new List<(int Index, Indexed Item)>();
			var claims = new SetClaims();

			for (var i = 0; i < upload.Archives.Count; i++)
			{
				var archive = upload.Archives[i];
				var (content, stream) = await ReadAsync(archive, cancellationToken).ConfigureAwait(false);
				if (content is null)
				{
					results[i] = new BeatmapsetImportResult(archive.Name, null, [], BeatmapsetImportFailure.Unreadable);
					continue;
				}

				Indexed item;
				await using (stream!.ConfigureAwait(false))
				await using (content.ConfigureAwait(false))
				{
					item = await PlanAsync(archive, null, content, null, cancellationToken).ConfigureAwait(false);
				}

				if (item.Locked)
				{
					results[i] = new BeatmapsetImportResult(archive.Name, null, [], BeatmapsetImportFailure.Locked);
					continue;
				}

				if (!claims.TryClaim(item.Sync))
				{
					results[i] = new BeatmapsetImportResult(archive.Name, null, [], BeatmapsetImportFailure.Duplicate);
					continue;
				}

				accepted.Add((i, item));
			}

			if (accepted.Count > 0)
			{
				// One upload is one transaction: every accepted set is stored, or none is.
				var storedSets = await beatmapsets.SyncAsync(
						new BeatmapsetSync(accepted.Select(entry => entry.Item.Sync).ToList(), []), cancellationToken)
					.ConfigureAwait(false);

				for (var i = 0; i < accepted.Count; i++)
				{
					var (index, item) = accepted[i];
					var set = storedSets[i];
					await storage.SaveAsync(set, item.Archive, cancellationToken).ConfigureAwait(false);
					await StoreFilesAsync(set, cancellationToken).ConfigureAwait(false);
					var storedBeatmaps = await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false);
					_events.Writer.TryWrite(new BeatmapsetImported(set, storedBeatmaps));
					results[index] = new BeatmapsetImportResult(item.Archive.Name, set, storedBeatmaps, null);
				}
			}

			// A difficulty uploaded on its own belongs to no beatmapset.
			return
			[
				.. results!,
				.. upload.Rejected.Select(name =>
					new BeatmapsetImportResult(name, null, [], BeatmapsetImportFailure.Loose))
			];
		}
		finally
		{
			await storage.DeleteAsync(upload, CancellationToken.None).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public async Task<BeatmapsetScanResult> ScanAsync(CancellationToken cancellationToken = default)
	{
		var packed = new List<BeatmapsetArchiveEntry>();
		try
		{
			return await ScanAllAsync(packed, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			// What was taken in has moved into a set's folder; the rest is not kept.
			foreach (var archive in packed)
				await storage.DeleteAsync(archive, CancellationToken.None).ConfigureAwait(false);
		}
	}

	/// <summary>Brings the server in step with every entry of the storage.</summary>
	/// <param name="packed">The archives packed from loose files during the scan, added to as they are packed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	private async Task<BeatmapsetScanResult> ScanAllAsync(List<BeatmapsetArchiveEntry> packed,
		CancellationToken cancellationToken)
	{
		var seen = new HashSet<int>();
		var queued = new List<(BeatmapsetArchiveEntry Archive, int? FolderId, Md5? Hash)>();
		var unchanged = new List<Beatmapset>();
		var changed = new List<(BeatmapsetSyncSet Sync, Beatmapset Set)>();
		var indexed = new List<Indexed>();
		var claims = new SetClaims();

		// A collection placed by hand is laid out first: each beatmapset in it becomes an entry of its own.
		var entries = new List<BeatmapsetEntry>();
		foreach (var entry in await storage.ListAsync(cancellationToken).ConfigureAwait(false))
			if (entry is BeatmapsetCollectionEntry collection)
				entries.AddRange(await storage.UnpackAsync(collection, cancellationToken).ConfigureAwait(false));
			else
				entries.Add(entry);

		foreach (var entry in entries)
		{
			switch (entry)
			{
				case BeatmapsetArchiveEntry loose:
					queued.Add((loose, null, null));
					break;

				case BeatmapsetFolderEntry { Exists: true, Archive: null } folder:
				{
					var packedArchive = await storage.PackAsync(folder, cancellationToken).ConfigureAwait(false);
					if (packedArchive is null) break;

					packed.Add(packedArchive);
					queued.Add((packedArchive, folder.Id, null));
					break;
				}

				case BeatmapsetFolderEntry { Exists: true, Archive: { } archive } folder:
				{
					Beatmapset? stored = null;
					if (folder.Id is { } id)
						stored = await beatmapsets.GetAsync(id, cancellationToken).ConfigureAwait(false);

					if (stored is not null &&
					    stored.Value.ArchiveSize == archive.Size &&
					    stored.Value.ArchiveModifiedAt == archive.ModifiedAt)
					{
						seen.Add(stored.Id);
						unchanged.Add(stored);
						break;
					}

					var hash = await HashAsync(archive, cancellationToken).ConfigureAwait(false);
					if (stored is not null && stored.Value.ArchiveHash == hash)
					{
						seen.Add(stored.Id);
						var sync = await SyncOfAsync(stored, archive, hash, cancellationToken).ConfigureAwait(false);
						claims.TryClaim(sync);
						changed.Add((sync, stored));
						break;
					}

					queued.Add((archive, folder.Id, hash));
					break;
				}
			}
		}

		foreach (var (archive, folderId, hash) in queued)
		{
			// An archive that cannot be read keeps its files; the set of the folder it lay in is not seen and is forgotten.
			var (content, stream) = await ReadAsync(archive, cancellationToken).ConfigureAwait(false);
			if (content is null) continue;

			await using (stream!.ConfigureAwait(false))
			await using (content.ConfigureAwait(false))
			{
				var item = await PlanAsync(archive, hash, content, folderId, cancellationToken).ConfigureAwait(false);
				if (item.Locked)
				{
					if (item.Existing is not null) seen.Add(item.Existing.Id);
					continue;
				}

				if (item.Existing is not null) seen.Add(item.Existing.Id);
				// Another archive of this scan already becomes that set; this one waits for the next scan.
				if (!claims.TryClaim(item.Sync)) continue;

				indexed.Add(item);
			}
		}

		var remove = new List<Beatmapset>();
		foreach (var set in await Paging.ListAllAsync(
				         page => beatmapsets.ListAsync(new BeatmapQuery(), page, cancellationToken), cancellationToken)
			         .ConfigureAwait(false))
			if (!seen.Contains(set.Id))
				remove.Add(set);

		var store = new List<BeatmapsetSyncSet>(indexed.Count + changed.Count);
		foreach (var item in indexed) store.Add(item.Sync);
		foreach (var (sync, _) in changed) store.Add(sync);

		var storedSets =
			await beatmapsets.SyncAsync(new BeatmapsetSync(store, remove), cancellationToken).ConfigureAwait(false);

		for (var i = 0; i < indexed.Count; i++)
		{
			var item = indexed[i];
			var set = storedSets[i];
			await storage.SaveAsync(set, item.Archive, cancellationToken).ConfigureAwait(false);
			await StoreFilesAsync(set, cancellationToken).ConfigureAwait(false);
			var storedBeatmaps = await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false);
			_events.Writer.TryWrite(new BeatmapsetImported(set, storedBeatmaps));
		}

		for (var i = 0; i < changed.Count; i++)
		{
			var set = storedSets[indexed.Count + i];
			if (await MissingFilesAsync(set, cancellationToken).ConfigureAwait(false))
				await StoreFilesAsync(set, cancellationToken).ConfigureAwait(false);
		}

		foreach (var set in unchanged)
			if (await MissingFilesAsync(set, cancellationToken).ConfigureAwait(false))
				await StoreFilesAsync(set, cancellationToken).ConfigureAwait(false);

		foreach (var set in remove)
			_events.Writer.TryWrite(new BeatmapsetDeleted(set));

		return new BeatmapsetScanResult(
			indexed.Count(item => item.Existing is null),
			indexed.Count(item => item.Existing is not null),
			remove.Count);
	}

	/// <inheritdoc />
	public async Task<BeatmapsetScanResult> ScanAsync(BeatmapsetEntry entry,
		CancellationToken cancellationToken = default)
	{
		switch (entry)
		{
			case BeatmapsetArchiveEntry archive:
				return await ScanArchiveAsync(archive, null, cancellationToken).ConfigureAwait(false);

			case BeatmapsetFolderEntry folder:
				return await ScanFolderAsync(folder, cancellationToken).ConfigureAwait(false);

			case BeatmapsetCollectionEntry collection:
			{
				var total = NoChange;
				foreach (var laidOut in await storage.UnpackAsync(collection, cancellationToken).ConfigureAwait(false))
				{
					var result = await ScanAsync(laidOut, cancellationToken).ConfigureAwait(false);
					total = new BeatmapsetScanResult(total.Imported + result.Imported, total.Updated + result.Updated,
						total.Forgotten + result.Forgotten);
				}

				return total;
			}

			default:
				return NoChange;
		}
	}

	/// <inheritdoc />
	public async Task<bool> DeleteAsync(Beatmapset set, CancellationToken cancellationToken = default)
	{
		if (set.Value.Locked)
			return false;

		await storage.DeleteAsync(set, cancellationToken).ConfigureAwait(false);
		await ForgetAsync(set, cancellationToken).ConfigureAwait(false);
		return true;
	}

	/// <summary>Brings the server in step with one folder of the storage.</summary>
	/// <param name="folder">The folder as it is now.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	/// <remarks>The folder keeps its files when its beatmapset is forgotten.</remarks>
	private async Task<BeatmapsetScanResult> ScanFolderAsync(BeatmapsetFolderEntry folder,
		CancellationToken cancellationToken)
	{
		if (folder is { Exists: true, Archive: { } archive })
			return await ScanArchiveAsync(archive, folder.Id, cancellationToken).ConfigureAwait(false);

		// A folder that holds no archive holds loose files, which are packed before they are read; a folder that is
		// gone packs to nothing.
		var packed = await storage.PackAsync(folder, cancellationToken).ConfigureAwait(false);
		if (packed is not null)
		{
			try
			{
				return await ScanArchiveAsync(packed, folder.Id, cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				await storage.DeleteAsync(packed, CancellationToken.None).ConfigureAwait(false);
			}
		}

		// The folder or its archive is gone: the beatmapset it held is forgotten.
		if (folder.Id is not { } id) return NoChange;

		var held = await beatmapsets.GetAsync(id, cancellationToken).ConfigureAwait(false);
		if (held is null) return NoChange;

		// The set may still have its archive in another folder (the server renames folders itself).
		var remaining = await storage.OpenArchiveAsync(held, true, cancellationToken).ConfigureAwait(false);
		if (remaining is not null)
		{
			await remaining.DisposeAsync().ConfigureAwait(false);
			return NoChange;
		}

		await ForgetAsync(held, cancellationToken).ConfigureAwait(false);
		return new BeatmapsetScanResult(0, 0, 1);
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
	private async Task<BeatmapsetScanResult> ScanArchiveAsync(BeatmapsetArchiveEntry archive, int? folderId,
		CancellationToken cancellationToken)
	{
		var hash = await HashAsync(archive, cancellationToken).ConfigureAwait(false);

		Beatmapset? stored = null;
		if (folderId is { } id)
			stored = await beatmapsets.GetAsync(id, cancellationToken).ConfigureAwait(false);

		// Unchanged. Missing files are not looked for here: the server's own writes into a set's folder come back as
		// changes while an index may still be writing; the full scan makes missing files again.
		if (stored is not null && stored.Value.ArchiveHash == hash) return NoChange;

		var (content, stream) = await ReadAsync(archive, cancellationToken).ConfigureAwait(false);
		if (content is null)
		{
			// The archive cannot be read: the set of the folder it lay in is forgotten, its files kept as they are.
			if (stored is null) return NoChange;

			await ForgetAsync(stored, cancellationToken).ConfigureAwait(false);
			return new BeatmapsetScanResult(0, 0, 1);
		}

		Indexed item;
		await using (stream!.ConfigureAwait(false))
		await using (content.ConfigureAwait(false))
		{
			item = await PlanAsync(archive, hash, content, folderId, cancellationToken).ConfigureAwait(false);
		}

		if (item.Locked) return NoChange;

		// The archive is closed first: it is moved into the set's folder.
		await StoreAsync(item, archive, cancellationToken).ConfigureAwait(false);
		return new BeatmapsetScanResult(item.Existing is null ? 1 : 0, item.Existing is null ? 0 : 1, 0);
	}

	/// <summary>Stores one planned beatmapset, makes every file it serves and announces it.</summary>
	/// <param name="item">The archive, as planned from what it contains.</param>
	/// <param name="archive">The archive, which becomes the beatmapset's own.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored beatmapset with its difficulties.</returns>
	private async Task<(Beatmapset Set, IReadOnlyList<Beatmap> Beatmaps)> StoreAsync(Indexed item,
		BeatmapsetArchiveEntry archive, CancellationToken cancellationToken)
	{
		var storedSets = await beatmapsets.SyncAsync(new BeatmapsetSync([item.Sync], []), cancellationToken)
			.ConfigureAwait(false);
		var set = storedSets[0];

		await storage.SaveAsync(set, archive, cancellationToken).ConfigureAwait(false);
		await StoreFilesAsync(set, cancellationToken).ConfigureAwait(false);

		var storedBeatmaps = await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false);
		_events.Writer.TryWrite(new BeatmapsetImported(set, storedBeatmaps));
		return (set, storedBeatmaps);
	}

	/// <summary>Makes every file a set serves from its archive, which is already stored.</summary>
	/// <param name="set">The beatmapset, whose archive is stored.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>The names are chosen the same way they were when the set was planned, so they match the records.</remarks>
	private async Task StoreFilesAsync(Beatmapset set, CancellationToken cancellationToken)
	{
		await storage.ClearAsync(set, cancellationToken).ConfigureAwait(false);

		var stream = await storage.OpenArchiveAsync(set, true, cancellationToken).ConfigureAwait(false);
		if (stream is null) return;

		var keep = false;
		try
		{
			var content = await reader.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
			keep = content is not null;
			if (content is null) return;

			await using (stream.ConfigureAwait(false))
			await using (content.ConfigureAwait(false))
			{
				var backgrounds = ChooseNames(content.Difficulties.Select(difficulty => difficulty.Background));
				var audio = ChooseNames(content.Difficulties.Select(difficulty => difficulty.Audio));
				var video = ChooseNames(content.Difficulties.Select(difficulty => difficulty.Video));

				foreach (var difficulty in content.Difficulties)
				{
					var hash = await HashAsync(difficulty.File, cancellationToken).ConfigureAwait(false);
					var osu = await difficulty.File.OpenAsync(cancellationToken).ConfigureAwait(false);
					await using (osu.ConfigureAwait(false))
					{
						await storage.SaveAsync(set, BeatmapsetFileKind.Version, $"{hash}.osu", osu, cancellationToken)
							.ConfigureAwait(false);
					}
				}

				var done = new HashSet<BeatmapsetFile>();
				foreach (var difficulty in content.Difficulties)
				{
					await SaveFileAsync(set, BeatmapsetFileKind.Background, difficulty.Background, backgrounds, done,
						cancellationToken).ConfigureAwait(false);
					await SaveFileAsync(set, BeatmapsetFileKind.Audio, difficulty.Audio, audio, done,
						cancellationToken).ConfigureAwait(false);
					await SaveFileAsync(set, BeatmapsetFileKind.Video, difficulty.Video, video, done,
						cancellationToken).ConfigureAwait(false);
				}

				if (content.Storyboard is not null)
				{
					var storyboard = await content.Storyboard.OpenAsync(cancellationToken).ConfigureAwait(false);
					await using (storyboard.ConfigureAwait(false))
					{
						await storage.SaveAsync(set, BeatmapsetFileKind.Storyboard, StoryboardFileName, storyboard,
							cancellationToken).ConfigureAwait(false);
					}
				}
			}
		}
		finally
		{
			if (!keep) await stream.DisposeAsync().ConfigureAwait(false);
		}

		await storage.SaveArchiveWithoutVideoAsync(set, cancellationToken).ConfigureAwait(false);
		var storedBeatmaps = await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false);
		await assets.BuildAsync(set, storedBeatmaps, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Stores one file of a set, once, under the name chosen for it.</summary>
	/// <param name="set">The beatmapset.</param>
	/// <param name="kind">What the file is.</param>
	/// <param name="file">The file of the set's archive, or <see langword="null" /> when the set has none of that kind.</param>
	/// <param name="names">The name chosen for each file.</param>
	/// <param name="done">The files already stored by this call; <paramref name="file" /> is added to it.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private async Task SaveFileAsync(Beatmapset set, BeatmapsetFileKind kind, BeatmapsetFile? file,
		IReadOnlyDictionary<BeatmapsetFile, string> names, HashSet<BeatmapsetFile> done,
		CancellationToken cancellationToken)
	{
		if (file is null || !done.Add(file)) return;

		var stream = await file.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using (stream.ConfigureAwait(false))
		{
			await storage.SaveAsync(set, kind, names[file], stream, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>Determines whether a stored beatmapset misses a file it serves.</summary>
	/// <param name="set">The stored beatmapset.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when its archive or any file made from it is not stored.</returns>
	private async Task<bool> MissingFilesAsync(Beatmapset set, CancellationToken cancellationToken)
	{
		var archive = await storage.OpenArchiveAsync(set, true, cancellationToken).ConfigureAwait(false);
		if (archive is null) return true;
		await archive.DisposeAsync().ConfigureAwait(false);

		var withoutVideo = await storage.OpenArchiveAsync(set, false, cancellationToken).ConfigureAwait(false);
		if (withoutVideo is null) return true;
		await withoutVideo.DisposeAsync().ConfigureAwait(false);

		if (set.Value.Storyboard &&
		    !await storage.ExistsAsync(set, BeatmapsetFileKind.Storyboard, StoryboardFileName, cancellationToken)
			    .ConfigureAwait(false))
			return true;

		foreach (var beatmap in await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false))
		{
			if (!await storage.ExistsAsync(set, BeatmapsetFileKind.Version, $"{beatmap.Value.Hash}.osu",
				    cancellationToken).ConfigureAwait(false))
				return true;

			if (beatmap.Value.Background is { } background &&
			    !await storage.ExistsAsync(set, BeatmapsetFileKind.Background, background, cancellationToken)
				    .ConfigureAwait(false))
				return true;

			if (beatmap.Value.Audio is { } audio)
			{
				if (!await storage.ExistsAsync(set, BeatmapsetFileKind.Audio, audio, cancellationToken)
					    .ConfigureAwait(false))
					return true;

				if (!await storage.ExistsAsync(set, BeatmapsetFileKind.Preview, audio, cancellationToken)
					    .ConfigureAwait(false))
					return true;
			}

			if (beatmap.Value.Video is { } video &&
			    !await storage.ExistsAsync(set, BeatmapsetFileKind.Video, video, cancellationToken)
				    .ConfigureAwait(false))
				return true;
		}

		return false;
	}

	/// <summary>Plans one archive's beatmapset: its records and the name of every file it serves.</summary>
	/// <param name="archive">The archive to plan.</param>
	/// <param name="archiveHash">The archive's hash when it is already known, or <see langword="null" />.</param>
	/// <param name="content">What the archive contains.</param>
	/// <param name="folderId">The id of the stored folder the archive lies in, or <see langword="null" /> when it lies loose.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The archive, the stored beatmapset it belongs to, and the set as read from it.</returns>
	private async Task<Indexed> PlanAsync(BeatmapsetArchiveEntry archive, Md5? archiveHash, BeatmapsetContent content,
		int? folderId, CancellationToken cancellationToken)
	{
		var setHash = archiveHash ?? await HashAsync(archive, cancellationToken).ConfigureAwait(false);

		var hashes = new Dictionary<BeatmapsetDifficulty, Md5>(content.Difficulties.Count);
		var stored = new Dictionary<BeatmapsetDifficulty, Beatmap?>(content.Difficulties.Count);
		foreach (var difficulty in content.Difficulties)
		{
			var hash = await HashAsync(difficulty.File, cancellationToken).ConfigureAwait(false);
			hashes[difficulty] = hash;
			stored[difficulty] = await beatmaps.GetAsync(hash, cancellationToken).ConfigureAwait(false);
		}

		var existing = await ResolveAsync(content, hashes, folderId, cancellationToken).ConfigureAwait(false);

		var backgrounds = ChooseNames(content.Difficulties.Select(difficulty => difficulty.Background));
		var audio = ChooseNames(content.Difficulties.Select(difficulty => difficulty.Audio));
		var video = ChooseNames(content.Difficulties.Select(difficulty => difficulty.Video));

		var synced = new List<BeatmapSyncData>(content.Difficulties.Count);

		foreach (var difficulty in content.Difficulties)
		{
			var analysis = await AnalyseAsync(difficulty, stored[difficulty], cancellationToken).ConfigureAwait(false);
			var background = difficulty.Background is null ? null : backgrounds[difficulty.Background];
			var sound = difficulty.Audio is null ? null : audio[difficulty.Audio];
			var clip = difficulty.Video is null ? null : video[difficulty.Video];

			synced.Add(new BeatmapSyncData(
				difficulty.OnlineId is > 0 ? difficulty.OnlineId : null,
				hashes[difficulty],
				difficulty.Version,
				analysis.Difficulty,
				analysis.Objects,
				background,
				sound,
				clip,
				difficulty.PreviewTime));
		}

		var sync = new BeatmapsetSyncSet(
			existing,
			existing is null && content.OnlineSetId is > 0 ? content.OnlineSetId : null,
			content.Artist,
			content.Title,
			content.Creator,
			setHash,
			archive.Size,
			archive.ModifiedAt,
			content.Storyboard is not null,
			synced);

		return new Indexed(archive, existing, sync,
			await TouchesLockedAsync(existing, content, hashes, stored, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>Determines whether storing what an archive holds would change something locked.</summary>
	/// <param name="existing">The stored set the archive replaces, or <see langword="null" />.</param>
	/// <param name="content">What the archive holds.</param>
	/// <param name="hashes">The hash of each difficulty.</param>
	/// <param name="stored">The stored beatmap with each difficulty's hash, or <see langword="null" />.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> when the set is locked, or a locked beatmap (or a beatmap of a locked set) would be
	///     taken over, changed or removed.
	/// </returns>
	private async Task<bool> TouchesLockedAsync(Beatmapset? existing, BeatmapsetContent content,
		Dictionary<BeatmapsetDifficulty, Md5> hashes, Dictionary<BeatmapsetDifficulty, Beatmap?> stored,
		CancellationToken cancellationToken)
	{
		if (existing is { Value.Locked: true }) return true;

		foreach (var difficulty in content.Difficulties)
		{
			var matched = stored[difficulty] ?? (difficulty.OnlineId is > 0
				? await beatmaps.GetAsync(difficulty.OnlineId.Value, cancellationToken).ConfigureAwait(false)
				: null);
			if (matched is null) continue;
			if (!matched.Value.Beatmapset.Equals(existing) && matched.Value.IsLocked()) return true;
			if (matched.Value.Locked && matched.Value.Hash != hashes[difficulty]) return true;
		}

		if (existing is null) return false;
		var kept = hashes.Values.ToHashSet();
		foreach (var beatmap in await beatmaps.ListAsync(existing, cancellationToken).ConfigureAwait(false))
			if (beatmap.Value.Locked && !kept.Contains(beatmap.Value.Hash))
				return true;

		return false;
	}

	/// <summary>Reads an archive lying in the storage, keeping it open while its content lives.</summary>
	/// <param name="archive">The archive to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     What the archive contains and the open stream it is read from; both <see langword="null" /> when it is not a
	///     readable beatmapset archive. The caller releases the content first and the stream after it.
	/// </returns>
	private async Task<(BeatmapsetContent? Content, Stream? Stream)> ReadAsync(BeatmapsetArchiveEntry archive,
		CancellationToken cancellationToken)
	{
		var stream = await archive.OpenAsync(cancellationToken).ConfigureAwait(false);
		var keep = false;
		try
		{
			var content = await reader.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
			keep = content is not null;
			return (content, stream);
		}
		finally
		{
			if (!keep) await stream.DisposeAsync().ConfigureAwait(false);
		}
	}

	/// <summary>Finds the stored beatmapset a read beatmapset belongs to.</summary>
	/// <param name="content">What was read.</param>
	/// <param name="hashes">The MD5 hash of each difficulty's file.</param>
	/// <param name="folderId">The id of the stored folder the content lies in, or <see langword="null" /> when it lies in none.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored beatmapset the content belongs to, or <see langword="null" /> when it is a new one.</returns>
	/// <remarks>
	///     The beatmapset is the stored one that already has a difficulty of the read set; otherwise the stored one with
	///     the folder's id; otherwise the stored one with the id the read set declares.
	/// </remarks>
	private async Task<Beatmapset?> ResolveAsync(BeatmapsetContent content,
		IReadOnlyDictionary<BeatmapsetDifficulty, Md5> hashes, int? folderId, CancellationToken cancellationToken)
	{
		foreach (var hash in hashes.Values)
		{
			var beatmap = await beatmaps.GetAsync(hash, cancellationToken).ConfigureAwait(false);
			if (beatmap is not null) return beatmap.Value.Beatmapset;
		}

		if (folderId is { } id)
		{
			var inFolder = await beatmapsets.GetAsync(id, cancellationToken).ConfigureAwait(false);
			if (inFolder is not null) return inFolder;
		}

		return content.OnlineSetId is > 0
			? await beatmapsets.GetAsync(content.OnlineSetId.Value, cancellationToken).ConfigureAwait(false)
			: null;
	}

	/// <summary>Describes a stored beatmapset as it is, with its archive's new size and modification time.</summary>
	/// <param name="set">The stored beatmapset, with its difficulties as they are stored.</param>
	/// <param name="archive">The stored set's archive, unchanged in content.</param>
	/// <param name="hash">The archive's hash.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private async Task<BeatmapsetSyncSet> SyncOfAsync(Beatmapset set, BeatmapsetArchiveEntry archive, Md5 hash,
		CancellationToken cancellationToken)
	{
		var synced = new List<BeatmapSyncData>();
		foreach (var beatmap in await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false))
			synced.Add(new BeatmapSyncData(
				beatmap.IsLocallyIngested ? null : beatmap.Id,
				beatmap.Value.Hash,
				beatmap.Value.Version,
				beatmap.Value.Difficulty,
				beatmap.Value.Objects,
				beatmap.Value.Background,
				beatmap.Value.Audio,
				beatmap.Value.Video,
				beatmap.Value.PreviewTime));

		return new BeatmapsetSyncSet(set, null, set.Value.Artist, set.Value.Title, set.Value.Creator, hash,
			archive.Size, archive.ModifiedAt, set.Value.Storyboard, synced);
	}

	/// <summary>Forgets a stored beatmapset, leaving the files of its folder as they are.</summary>
	/// <param name="set">The beatmapset to forget.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private async Task ForgetAsync(Beatmapset set, CancellationToken cancellationToken)
	{
		await beatmapsets.SyncAsync(new BeatmapsetSync([], [set]), cancellationToken).ConfigureAwait(false);
		_events.Writer.TryWrite(new BeatmapsetDeleted(set));
	}

	/// <summary>Chooses the plain name each file of one kind is stored under.</summary>
	/// <param name="files">The files of one kind, in difficulty order; a file used by several difficulties comes once.</param>
	/// <returns>One chosen name per file; two different files never get the same name.</returns>
	private static Dictionary<BeatmapsetFile, string> ChooseNames(IEnumerable<BeatmapsetFile?> files)
	{
		var names = new Dictionary<BeatmapsetFile, string>();
		var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var file in files)
		{
			if (file is null || names.ContainsKey(file)) continue;

			var plain = PlainName(file.Name);
			var stem = Path.GetFileNameWithoutExtension(plain);
			var extension = Path.GetExtension(plain);
			var name = plain;
			for (var suffix = 2; !taken.Add(name); suffix++)
				name = $"{stem}-{suffix}{extension}";

			names[file] = name;
		}

		return names;
	}

	/// <summary>A file's plain name, with every character a file name may not have replaced.</summary>
	/// <param name="name">The file's name relative to the set.</param>
	/// <returns>The plain name, safe to store a file under; a name left empty by the replacement is <c>file</c>.</returns>
	private static string PlainName(string name)
	{
		var plain = Path.GetFileName(name);
		var builder = new StringBuilder(plain.Length);
		foreach (var character in plain)
			builder.Append(character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'
				or < ' '
				? '_'
				: character);

		// Windows drops trailing dots and spaces and keeps device names such as CON or NUL.txt for itself.
		var result = builder.ToString().TrimEnd('.', ' ');
		if (result.Length == 0) return "file";
		var dot = result.IndexOf('.');
		return ReservedName.IsMatch(dot < 0 ? result : result[..dot]) ? "_" + result : result;
	}

	/// <summary>Computes the MD5 hash of an archive's content.</summary>
	/// <param name="archive">The archive, read to its end; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private static async Task<Md5> HashAsync(BeatmapsetArchiveEntry archive, CancellationToken cancellationToken)
	{
		var stream = await archive.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using (stream.ConfigureAwait(false))
		{
			return await Md5.ComputeAsync(stream, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>Computes the MD5 hash of a set file's content.</summary>
	/// <param name="file">The file, read to its end; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private static async Task<Md5> HashAsync(BeatmapsetFile file, CancellationToken cancellationToken)
	{
		var stream = await file.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using (stream.ConfigureAwait(false))
		{
			return await Md5.ComputeAsync(stream, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	///     Analyses a difficulty, reusing the analysis of the stored beatmap with the same file when it has a star
	///     rating.
	/// </summary>
	/// <param name="difficulty">The difficulty to analyse.</param>
	/// <param name="stored">The stored beatmap of the same file, or <see langword="null" /> when there is none.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>A difficulty that cannot be analysed gets a zero star rating and no objects.</remarks>
	private async Task<BeatmapAnalysis> AnalyseAsync(BeatmapsetDifficulty difficulty, Beatmap? stored,
		CancellationToken cancellationToken)
	{
		if (stored is { Value.Difficulty.Star: > 0 })
			return new BeatmapAnalysis(stored.Value.Difficulty, stored.Value.Objects);

		try
		{
			var stream = await difficulty.File.OpenAsync(cancellationToken).ConfigureAwait(false);
			await using (stream.ConfigureAwait(false))
			{
				return analyser.Analyze(stream, difficulty.Mode, GameMods.NoMod);
			}
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return EmptyAnalysis(difficulty.Mode);
		}
	}

	/// <summary>A zero difficulty and no objects in a game mode.</summary>
	/// <param name="mode">The game mode.</param>
	private static BeatmapAnalysis EmptyAnalysis(GameMode mode)
	{
		var objects = BeatmapObjects.Empty(mode);
		return new BeatmapAnalysis(new Difficulty(mode, 0, TimeSpan.Zero, 0, 0, 0, 0, 0), objects);
	}

	/// <summary>One archive read for indexing: the stored beatmapset it belongs to and its records.</summary>
	/// <param name="Archive">The archive, which becomes the beatmapset's own.</param>
	/// <param name="Existing">The stored beatmapset it belongs to, or <see langword="null" /> when it is a new one.</param>
	/// <param name="Sync">The beatmapset as read from the archive.</param>
	/// <param name="Locked">Whether storing it would change something locked, so it is not stored.</param>
	private sealed record Indexed(
		BeatmapsetArchiveEntry Archive,
		Beatmapset? Existing,
		BeatmapsetSyncSet Sync,
		bool Locked);

	/// <summary>The beatmapsets and difficulties already given to an archive by one import or scan.</summary>
	/// <remarks>Two archives of one import or scan never become the same beatmapset nor share a difficulty: the first wins.</remarks>
	private sealed class SetClaims
	{
		private readonly HashSet<Md5> _beatmaps = [];
		private readonly HashSet<int> _sets = [];

		/// <summary>Gives a beatmapset and its difficulties to one archive.</summary>
		/// <param name="sync">The beatmapset as read from the archive.</param>
		/// <returns>
		///     <see langword="false" /> when an earlier archive already has the beatmapset or one of its difficulties, or
		///     the archive repeats a difficulty; nothing is claimed then.
		/// </returns>
		public bool TryClaim(BeatmapsetSyncSet sync)
		{
			var id = sync.Existing?.Id ?? sync.OnlineId;
			var hashes = sync.Beatmaps.Select(beatmap => beatmap.Hash).ToList();
			if ((id is { } taken && _sets.Contains(taken)) || hashes.Count != hashes.Distinct().Count() ||
			    hashes.Any(_beatmaps.Contains))
				return false;

			if (id is { } claimed) _sets.Add(claimed);
			_beatmaps.UnionWith(hashes);
			return true;
		}
	}
}
