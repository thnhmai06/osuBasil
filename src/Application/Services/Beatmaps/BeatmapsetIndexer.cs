using System.Text;
using System.Text.RegularExpressions;
using Basil.Application.Storage.Common;
using Basil.Application.Storage.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;
using Basil.Domain.Mechanics;

namespace Basil.Application.Services.Beatmaps;

/// <summary>
///     Imports, deletes and keeps track of the beatmapsets the server has.
/// </summary>
internal sealed partial class BeatmapsetIndexer(
	IBeatmapsetReader reader,
	IBeatmapAnalyser analyser,
	IBeatmapAssets assets,
	IBeatmapsetRepository beatmapsets,
	IBeatmapRepository beatmaps,
	IBeatmapsetStorage storage)
{
	/// <summary>The name passed when a storyboard file is stored or looked for; its kind decides where it lies.</summary>
	private const string StoryboardFileName = "storyboard.osb";

	/// <summary>A scan that changed nothing.</summary>
	private static readonly BeatmapsetScanResult NoChange = new(0, 0, 0);

	/// <summary>The names Windows keeps for devices, whatever their extension.</summary>
	private static readonly Regex ReservedName = ReservedPattern();

	/// <inheritdoc />
	/// <inheritdoc />
	/// <inheritdoc />
	public async Task<(BeatmapsetImportResult Result, BeatmapsetIndexed? Imported)> ImportAsync(Stream archive,
		CancellationToken cancellationToken = default)
	{
		var uploaded = await storage.SaveAsync(archive, cancellationToken).ConfigureAwait(false);
		try
		{
			var (content, stream) = await ReadAsync(uploaded, cancellationToken).ConfigureAwait(false);
			if (content is null)
				return (new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Unreadable), null);

			Indexed item;
			await using (stream!.ConfigureAwait(false))
			await using (content.ConfigureAwait(false))
			{
				item = await PlanAsync(uploaded, null, content, null, cancellationToken).ConfigureAwait(false);
			}

			if (item.Locked)
				return (new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Locked), null);

			// A set whose difficulties repeat one another cannot be stored.
			if (!new SetClaims().TryClaim(item.Change))
				return (new BeatmapsetImportResult(null, [], BeatmapsetImportFailure.Unreadable), null);

			// The archive is closed first: it is moved into the set's folder.
			var imported = await StoreAsync(item, cancellationToken).ConfigureAwait(false);
			return (new BeatmapsetImportResult(imported.Set, imported.Beatmaps, null), imported);
		}
		finally
		{
			// Taken in, the archive has moved into its set's folder; rejected, nothing of it is kept.
			await storage.DeleteAsync(uploaded, CancellationToken.None).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public async Task<BeatmapsetScanOutcome> ScanAsync(CancellationToken cancellationToken = default)
	{
		var packed = new List<BeatmapsetArchive>();
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

	/// <inheritdoc />
	public async Task<BeatmapsetScanOutcome> ScanAsync(BeatmapsetSource entry,
		CancellationToken cancellationToken = default)
	{
		return entry switch
		{
			BeatmapsetArchive archive => await ScanArchiveAsync(archive, null, cancellationToken).ConfigureAwait(false),
			BeatmapsetFolder folder => await ScanFolderAsync(folder, cancellationToken).ConfigureAwait(false),
			_ => EmptyOutcome(NoChange)
		};
	}

	/// <summary>Brings the server in step with every entry of the storage.</summary>
	/// <param name="packed">The archives packed from loose files during the scan, added to as they are packed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>How many beatmapsets were taken in, updated and forgotten.</returns>
	private async Task<BeatmapsetScanOutcome> ScanAllAsync(
		List<BeatmapsetArchive> packed, CancellationToken cancellationToken)
	{
		var seen = new HashSet<int>();
		var queued = new List<(BeatmapsetArchive Archive, int? FolderId, ContentFingerprint? Fingerprint)>();
		var unchanged = new List<BeatmapsetRecord>();
		var changed = new List<(BeatmapsetChange Change, BeatmapsetRecord Set)>();
		var indexed = new List<Indexed>();
		var imported = new List<BeatmapsetIndexed>();
		var claims = new SetClaims();

		foreach (var entry in await storage.ListAsync(cancellationToken).ConfigureAwait(false))
			switch (entry)
			{
				case BeatmapsetArchive loose:
					queued.Add((loose, null, null));
					break;

				case BeatmapsetFolder { Exists: true, Archive: null } folder:
				{
					var packedArchive = await storage.PackAsync(folder, cancellationToken).ConfigureAwait(false);
					if (packedArchive is null) break;

					packed.Add(packedArchive);
					queued.Add((packedArchive, folder.Id, null));
					break;
				}

				case BeatmapsetFolder { Exists: true, Archive: { } archive } folder:
				{
					BeatmapsetRecord? stored = null;
					if (folder.Id is { } id)
						stored = await beatmapsets.GetAsync(id, cancellationToken).ConfigureAwait(false);

					if (stored is not null && stored.Fingerprint.Stamp == archive.Stamp)
					{
						seen.Add(stored.Id);
						unchanged.Add(stored);
						break;
					}

					var fingerprint = await archive.FingerprintAsync(cancellationToken).ConfigureAwait(false);
					if (stored is not null && stored.Fingerprint.Hash == fingerprint.Hash)
					{
						seen.Add(stored.Id);
						var change = await ChangeOfAsync(stored, fingerprint, cancellationToken).ConfigureAwait(false);
						claims.TryClaim(change);
						changed.Add((change, stored));
						break;
					}

					queued.Add((archive, folder.Id, fingerprint));
					break;
				}
			}

		foreach (var (archive, folderId, fingerprint) in queued)
		{
			// An archive that cannot be read keeps its files; the set of the folder it lay in is not seen and is forgotten.
			var (content, stream) = await ReadAsync(archive, cancellationToken).ConfigureAwait(false);
			if (content is null) continue;

			await using (stream!.ConfigureAwait(false))
			await using (content.ConfigureAwait(false))
			{
				var item = await PlanAsync(archive, fingerprint, content, folderId, cancellationToken)
					.ConfigureAwait(false);
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

		var remove = (
				await Paging.ListAllAsync(page => beatmapsets.ListAsync(
					new BeatmapQuery(), page, cancellationToken), cancellationToken).ConfigureAwait(false))
			.Where(set => !seen.Contains(set.Id))
			.ToList();

		var store = indexed.Select(x => x.Change)
			.Concat(changed.Select(x => x.Change))
			.ToList();

		var storedSets =
			await beatmapsets.CommitAsync(new BeatmapsetChanges(store, remove), cancellationToken)
				.ConfigureAwait(false);

		for (var i = 0; i < indexed.Count; i++)
		{
			var item = indexed[i];
			var set = storedSets[i];
			await storage.SaveAsync(set, item.Archive, cancellationToken).ConfigureAwait(false);
			await StoreFilesAsync(set, cancellationToken).ConfigureAwait(false);
			var storedBeatmaps = await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false);
			imported.Add(new BeatmapsetIndexed(set, storedBeatmaps));
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

		return new BeatmapsetScanOutcome(
			new BeatmapsetScanResult(
				indexed.Count(item => item.Existing is null),
				indexed.Count(item => item.Existing is not null),
				remove.Count),
			imported,
			remove);
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
			return await ScanArchiveAsync(archive, folder.Id, cancellationToken).ConfigureAwait(false);

		// A folder that holds no archive holds loose files, which are packed before they are read; a folder that is
		// gone packs to nothing.
		var packed = await storage.PackAsync(folder, cancellationToken).ConfigureAwait(false);
		if (packed is not null)
			try
			{
				return await ScanArchiveAsync(packed, folder.Id, cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				await storage.DeleteAsync(packed, CancellationToken.None).ConfigureAwait(false);
			}

		// The folder or its archive is gone: the beatmapset it held is forgotten.
		if (folder.Id is not { } id) return EmptyOutcome(NoChange);

		var held = await beatmapsets.GetAsync(id, cancellationToken).ConfigureAwait(false);
		if (held is null) return EmptyOutcome(NoChange);

		// The set may still have its archive in another folder (the server renames folders itself).
		var remaining = await storage.OpenArchiveAsync(held, true, cancellationToken).ConfigureAwait(false);
		if (remaining is not null)
		{
			await remaining.DisposeAsync().ConfigureAwait(false);
			return EmptyOutcome(NoChange);
		}

		var deleted = await ForgetAsync(held, cancellationToken).ConfigureAwait(false);
		return new BeatmapsetScanOutcome(new BeatmapsetScanResult(0, 0, 1), [], [deleted]);
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
		var fingerprint = await archive.FingerprintAsync(cancellationToken).ConfigureAwait(false);

		BeatmapsetRecord? stored = null;
		if (folderId is { } id)
			stored = await beatmapsets.GetAsync(id, cancellationToken).ConfigureAwait(false);

		// Unchanged. Missing files are not looked for here: the server's own writes into a set's folder come back as
		// changes while an index may still be writing; the full scan makes missing files again.
		if (stored is not null && stored.Fingerprint.Hash == fingerprint.Hash) return EmptyOutcome(NoChange);

		var (content, stream) = await ReadAsync(archive, cancellationToken).ConfigureAwait(false);
		if (content is null)
		{
			// The archive cannot be read: the set of the folder it lay in is forgotten, its files kept as they are.
			if (stored is null) return EmptyOutcome(NoChange);

			var deleted = await ForgetAsync(stored, cancellationToken).ConfigureAwait(false);
			return new BeatmapsetScanOutcome(new BeatmapsetScanResult(0, 0, 1), [], [deleted]);
		}

		Indexed item;
		await using (stream!.ConfigureAwait(false))
		await using (content.ConfigureAwait(false))
		{
			item = await PlanAsync(archive, fingerprint, content, folderId, cancellationToken).ConfigureAwait(false);
		}

		if (item.Locked) return EmptyOutcome(NoChange);

		// The archive is closed first: it is moved into the set's folder.
		var imported = await StoreAsync(item, cancellationToken).ConfigureAwait(false);
		return new BeatmapsetScanOutcome(
			new BeatmapsetScanResult(item.Existing is null ? 1 : 0, item.Existing is null ? 0 : 1, 0),
			[imported], []);
	}

	/// <summary>Stores one planned beatmapset and makes every file it serves.</summary>
	/// <param name="item">The archive, as planned from what it contains.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored beatmapset with its difficulties.</returns>
	private async Task<BeatmapsetIndexed> StoreAsync(Indexed item,
		CancellationToken cancellationToken)
	{
		var storedSets = await beatmapsets.CommitAsync(new BeatmapsetChanges([item.Change], []), cancellationToken)
			.ConfigureAwait(false);
		var set = storedSets[0];

		await storage.SaveAsync(set, item.Archive, cancellationToken).ConfigureAwait(false);
		await StoreFilesAsync(set, cancellationToken).ConfigureAwait(false);

		var storedBeatmaps = await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false);
		return new BeatmapsetIndexed(set, storedBeatmaps);
	}

	/// <summary>Forgets a stored beatmapset.</summary>
	/// <param name="set">The beatmapset to forget.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The forgotten beatmapset.</returns>
	private async Task<BeatmapsetRecord> ForgetAsync(BeatmapsetRecord set, CancellationToken cancellationToken)
	{
		await beatmapsets.CommitAsync(new BeatmapsetChanges([], [set]), cancellationToken).ConfigureAwait(false);
		return set;
	}

	private static BeatmapsetScanOutcome EmptyOutcome(BeatmapsetScanResult result)
	{
		return new BeatmapsetScanOutcome(result, [], []);
	}

	/// <summary>Makes every file a set serves from its archive, which is already stored.</summary>
	/// <param name="set">The beatmapset, whose archive is stored.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>The names are chosen the same way they were when the set was planned, so they match the records.</remarks>
	private async Task StoreFilesAsync(BeatmapsetRecord set, CancellationToken cancellationToken)
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
						await storage.SaveAsync(set, BeatmapsetFileType.Version, $"{hash}.osu", osu, cancellationToken)
							.ConfigureAwait(false);
					}
				}

				var done = new HashSet<BeatmapsetContentFile>();
				foreach (var difficulty in content.Difficulties)
				{
					await SaveFileAsync(set, BeatmapsetFileType.Background, difficulty.Background, backgrounds, done,
						cancellationToken).ConfigureAwait(false);
					await SaveFileAsync(set, BeatmapsetFileType.Audio, difficulty.Audio, audio, done,
						cancellationToken).ConfigureAwait(false);
					await SaveFileAsync(set, BeatmapsetFileType.Video, difficulty.Video, video, done,
						cancellationToken).ConfigureAwait(false);
				}

				if (content.Storyboard is not null)
				{
					var storyboard = await content.Storyboard.OpenAsync(cancellationToken).ConfigureAwait(false);
					await using (storyboard.ConfigureAwait(false))
					{
						await storage.SaveAsync(set, BeatmapsetFileType.Storyboard, StoryboardFileName, storyboard,
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
	/// <param name="type">What the file is.</param>
	/// <param name="file">The file of the set's archive, or <see langword="null" /> when the set has none of that kind.</param>
	/// <param name="names">The name chosen for each file.</param>
	/// <param name="done">The files already stored by this call; <paramref name="file" /> is added to it.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private async Task SaveFileAsync(BeatmapsetRecord set, BeatmapsetFileType type, BeatmapsetContentFile? file,
		IReadOnlyDictionary<BeatmapsetContentFile, string> names, HashSet<BeatmapsetContentFile> done,
		CancellationToken cancellationToken)
	{
		if (file is null || !done.Add(file)) return;

		var stream = await file.OpenAsync(cancellationToken).ConfigureAwait(false);
		await using (stream.ConfigureAwait(false))
		{
			await storage.SaveAsync(set, type, names[file], stream, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>Determines whether a stored beatmapset misses a file it serves.</summary>
	/// <param name="set">The stored beatmapset.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when its archive or any file made from it is not stored.</returns>
	private async Task<bool> MissingFilesAsync(BeatmapsetRecord set, CancellationToken cancellationToken)
	{
		var archive = await storage.OpenArchiveAsync(set, true, cancellationToken).ConfigureAwait(false);
		if (archive is null) return true;
		await archive.DisposeAsync().ConfigureAwait(false);

		var withoutVideo = await storage.OpenArchiveAsync(set, false, cancellationToken).ConfigureAwait(false);
		if (withoutVideo is null) return true;
		await withoutVideo.DisposeAsync().ConfigureAwait(false);

		if (set.Assets.HasStoryboard &&
		    !await storage.ExistsAsync(set, BeatmapsetFileType.Storyboard, StoryboardFileName, cancellationToken)
			    .ConfigureAwait(false))
			return true;

		foreach (var beatmap in await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false))
		{
			if (!await storage.ExistsAsync(set, BeatmapsetFileType.Version, $"{beatmap.Hash}.osu",
				    cancellationToken).ConfigureAwait(false))
				return true;

			if (beatmap.Assets.BackgroundFile is { } background &&
			    !await storage.ExistsAsync(set, BeatmapsetFileType.Background, background, cancellationToken)
				    .ConfigureAwait(false))
				return true;

			if (beatmap.Assets.AudioFile is { } audio)
			{
				if (!await storage.ExistsAsync(set, BeatmapsetFileType.Audio, audio, cancellationToken)
					    .ConfigureAwait(false))
					return true;

				if (!await storage.ExistsAsync(set, BeatmapsetFileType.Preview, audio, cancellationToken)
					    .ConfigureAwait(false))
					return true;
			}

			if (beatmap.Assets.VideoFile is { } video &&
			    !await storage.ExistsAsync(set, BeatmapsetFileType.Video, video, cancellationToken)
				    .ConfigureAwait(false))
				return true;
		}

		return false;
	}

	/// <summary>Plans one archive's beatmapset: its records and the name of every file it serves.</summary>
	/// <param name="archive">The archive to plan.</param>
	/// <param name="fingerprint">The archive's fingerprint when it is already taken, or <see langword="null" />.</param>
	/// <param name="content">What the archive contains.</param>
	/// <param name="folderId">The id of the stored folder the archive lies in, or <see langword="null" /> when it lies loose.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The archive, the stored beatmapset it belongs to, and the set as read from it.</returns>
	private async Task<Indexed> PlanAsync(BeatmapsetArchive archive, ContentFingerprint? fingerprint,
		BeatmapsetContent content,
		int? folderId, CancellationToken cancellationToken)
	{
		var setFingerprint = fingerprint ?? await archive.FingerprintAsync(cancellationToken).ConfigureAwait(false);

		var hashes = new Dictionary<BeatmapsetDifficulty, Md5>(content.Difficulties.Count);
		var stored = new Dictionary<BeatmapsetDifficulty, BeatmapRecord?>(content.Difficulties.Count);
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

		var synced = new List<BeatmapData>(content.Difficulties.Count);

		foreach (var difficulty in content.Difficulties)
		{
			var analysis = await AnalyseAsync(difficulty, stored[difficulty], cancellationToken).ConfigureAwait(false);
			var background = difficulty.Background is null ? null : backgrounds[difficulty.Background];
			var sound = difficulty.Audio is null ? null : audio[difficulty.Audio];
			var clip = difficulty.Video is null ? null : video[difficulty.Video];

			synced.Add(new BeatmapData(
				difficulty.OnlineId is > 0 ? difficulty.OnlineId : null,
				hashes[difficulty],
				new BeatmapMetadata
				{
					Version = difficulty.Version, Difficulty = analysis.Difficulty, Objects = analysis.Objects
				},
				new BeatmapAssets
				{
					HasStoryboard = difficulty.Storyboard,
					BackgroundFile = background,
					AudioFile = sound,
					VideoFile = clip,
					PreviewTime = difficulty.PreviewTime
				}));
		}

		var data = new BeatmapsetData(
			existing is null && content.OnlineSetId is > 0 ? content.OnlineSetId : null,
			setFingerprint,
			new BeatmapsetMetadata { Artist = content.Artist, Title = content.Title, Creator = content.Creator },
			new BeatmapsetAssets { HasStoryboard = content.Storyboard is not null },
			synced);

		return new Indexed(archive, existing, data,
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
	private async Task<bool> TouchesLockedAsync(BeatmapsetRecord? existing, BeatmapsetContent content,
		Dictionary<BeatmapsetDifficulty, Md5> hashes, Dictionary<BeatmapsetDifficulty, BeatmapRecord?> stored,
		CancellationToken cancellationToken)
	{
		if (existing is { Locked: true }) return true;

		foreach (var difficulty in content.Difficulties)
		{
			var matched = stored[difficulty] ?? (difficulty.OnlineId is > 0
				? await beatmaps.GetAsync(difficulty.OnlineId.Value, cancellationToken).ConfigureAwait(false)
				: null);
			if (matched is null) continue;
			if (!matched.Beatmapset.Equals(existing) && matched.IsLocked()) return true;
			if (matched.Locked && matched.Hash != hashes[difficulty]) return true;
		}

		if (existing is null) return false;
		var kept = hashes.Values.ToHashSet();
		return (await beatmaps.ListAsync(existing, cancellationToken).ConfigureAwait(false)).Any(beatmap =>
			beatmap.Locked && !kept.Contains(beatmap.Hash));
	}

	/// <summary>Reads an archive lying in the storage, keeping it open while its content lives.</summary>
	/// <param name="archive">The archive to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     What the archive contains and the open stream it is read from; both <see langword="null" /> when it is not a
	///     readable beatmapset archive. The caller releases the content first and the stream after it.
	/// </returns>
	private async Task<(BeatmapsetContent? Content, Stream? Stream)> ReadAsync(BeatmapsetArchive archive,
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
	/// <param name="folderId">
	///     The id of the stored folder the content lies in, or <see langword="null" /> when it lies in
	///     none.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored beatmapset the content belongs to, or <see langword="null" /> when it is a new one.</returns>
	/// <remarks>
	///     The beatmapset is the stored one that already has a difficulty of the read set; otherwise the stored one with
	///     the folder's id; otherwise the stored one with the id the read set declares.
	/// </remarks>
	private async Task<BeatmapsetRecord?> ResolveAsync(BeatmapsetContent content,
		IReadOnlyDictionary<BeatmapsetDifficulty, Md5> hashes, int? folderId, CancellationToken cancellationToken)
	{
		foreach (var hash in hashes.Values)
		{
			var beatmap = await beatmaps.GetAsync(hash, cancellationToken).ConfigureAwait(false);
			if (beatmap is not null) return beatmap.Beatmapset;
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

	/// <summary>Describes a stored beatmapset as it is, with its archive's new fingerprint.</summary>
	/// <param name="set">The stored beatmapset, with its difficulties as they are stored.</param>
	/// <param name="fingerprint">The fingerprint of the set's archive, unchanged in content.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private async Task<BeatmapsetChange> ChangeOfAsync(BeatmapsetRecord set, ContentFingerprint fingerprint,
		CancellationToken cancellationToken)
	{
		var synced = (
			from beatmap in await beatmaps.ListAsync(set, cancellationToken).ConfigureAwait(false)
			select new BeatmapData(beatmap.IsLocallyIngested ? null : beatmap.Id, beatmap.Hash, beatmap.Metadata,
				beatmap.Assets)).ToList();

		return new BeatmapsetChange(set, new BeatmapsetData(null, fingerprint, set.Metadata, set.Assets, synced));
	}

	/// <summary>Chooses the plain name each file of one kind is stored under.</summary>
	/// <param name="files">The files of one kind, in difficulty order; a file used by several difficulties comes once.</param>
	/// <returns>One chosen name per file; two different files never get the same name.</returns>
	private static Dictionary<BeatmapsetContentFile, string> ChooseNames(IEnumerable<BeatmapsetContentFile?> files)
	{
		var names = new Dictionary<BeatmapsetContentFile, string>();
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
			builder.Append(character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' or < ' '
				? '_'
				: character);

		// Windows drops trailing dots and spaces and keeps device names such as CON or NUL.txt for itself.
		var result = builder.ToString().TrimEnd('.', ' ');
		if (result.Length == 0) return "file";
		var dot = result.IndexOf('.');
		return ReservedName.IsMatch(dot < 0 ? result : result[..dot]) ? "_" + result : result;
	}

	/// <summary>Computes the MD5 hash of a set file's content.</summary>
	/// <param name="file">The file, read to its end; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private static async Task<Md5> HashAsync(BeatmapsetContentFile file, CancellationToken cancellationToken)
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
	private async Task<BeatmapAnalysis> AnalyseAsync(BeatmapsetDifficulty difficulty, BeatmapRecord? stored,
		CancellationToken cancellationToken)
	{
		if (stored is { Metadata.Difficulty.Star: > 0 })
			return new BeatmapAnalysis(stored.Metadata.Difficulty, stored.Metadata.Objects);

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
		return new BeatmapAnalysis(new BeatmapDifficulty(mode, 0, TimeSpan.Zero, 0, 0, 0, 0, 0), objects);
	}

	/// <summary>One archive read for indexing: the stored beatmapset it belongs to and its records.</summary>
	/// <param name="Archive">The archive, which becomes the beatmapset's own.</param>
	/// <param name="Existing">The stored beatmapset it belongs to, or <see langword="null" /> when it is a new one.</param>
	/// <param name="Data">The beatmapset as read from the archive.</param>
	/// <param name="Locked">Whether storing it would change something locked, so it is not stored.</param>
	private sealed record Indexed(
		BeatmapsetArchive Archive,
		BeatmapsetRecord? Existing,
		BeatmapsetData Data,
		bool Locked)
	{
		/// <summary>Gets the change that stores the beatmapset.</summary>
		public BeatmapsetChange Change => new(Existing, Data);
	}

	/// <summary>The beatmapsets and difficulties already given to an archive by one scan or import.</summary>
	/// <remarks>Two archives of one scan never become the same beatmapset nor share a difficulty: the first wins.</remarks>
	private sealed class SetClaims
	{
		private readonly HashSet<Md5> _beatmaps = [];
		private readonly HashSet<int> _sets = [];

		/// <summary>Gives a beatmapset and its difficulties to one archive.</summary>
		/// <param name="change">The beatmapset to store.</param>
		/// <returns>
		///     <see langword="false" /> when an earlier archive already has the beatmapset or one of its difficulties, or
		///     the archive repeats a difficulty; nothing is claimed then.
		/// </returns>
		public bool TryClaim(BeatmapsetChange change)
		{
			var id = change.Existing?.Id ?? change.Data.OnlineId;
			var hashes = change.Data.Beatmaps.Select(beatmap => beatmap.Hash).ToList();
			if ((id is { } taken && _sets.Contains(taken)) || hashes.Count != hashes.Distinct().Count() ||
			    hashes.Any(_beatmaps.Contains))
				return false;

			if (id is { } claimed) _sets.Add(claimed);
			_beatmaps.UnionWith(hashes);
			return true;
		}
	}

	[GeneratedRegex(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[0-9¹²³]|LPT[0-9¹²³]) *$",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex ReservedPattern();
}