using System.IO.Compression;
using System.Text.RegularExpressions;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Storage;
using Basil.Application.Configurations;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Storyboards;
using Beatmap = Basil.Domain.Beatmaps.Beatmap;
using LazerBeatmap = osu.Game.Beatmaps.Beatmap;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>
///     Keeps the DB in sync with <see cref="StorageOptions.BeatmapsetsPath" />: a beatmapset is a
///     single loose <c>"{BeatmapsetId} {Artist} - {Title}.osz"</c> archive, kept permanently
///     (ADR-006's ".osz direct storage" design) — individual assets (backgrounds, audio, .osu
///     files) are extracted on demand into <see cref="BeatmapsetAssetCache" /> rather than into a
///     folder. A beatmapset extracted before this design (one subfolder per set, holding the full
///     original .osz contents as-is) is still recognized and reconciled during the migration
///     window covered by the background migration pass; both a loose <c>.osz</c> and a
///     legacy extracted folder count as "this beatmapset still exists" until migration converts
///     the folder. A single loose ".osu" file has no set context on its own and is never ingested.
///     <see cref="BeatmapWatcherService" /> is the incremental caller (per-file/per-folder
///     reconciliation on every filesystem change); <see cref="ReconcileAllAsync" /> is the full-pass
///     caller.
/// </summary>
public sealed partial class BeatmapIngestionService(
	IBeatmapRepository beatmaps,
	IBeatmapsetRepository beatmapsetRepository,
	IOsuCalculator osuCalculator,
	IOptions<StorageOptions> options,
	IResponseCache cache,
	BeatmapsetAssetCache assetCache,
	ILogger<BeatmapIngestionService> logger)
{
	/// <summary>
	///     Marker infix for a beatmapset folder mid-deletion: a folder whose name carries this infix
	///     is never treated as a live beatmapset.
	/// </summary>
	public const string DeletedFolderInfix = ".deleted_";

	private static readonly char[] IllegalFilenameChars = Path.GetInvalidFileNameChars();
	private static readonly Regex LeadingIdPattern = LeadingIdRegex();

	/// <summary>
	///     Builds the conventional folder name for a beatmapset: <c>"{Id} {Artist} - {Title}"</c>, with
	///     characters invalid in filenames stripped out.
	/// </summary>
	public static string BeatmapsetFolderName(Beatmapset beatmapset)
	{
		return Sanitize($"{beatmapset.Id} {beatmapset.Artist} - {beatmapset.Title}");
	}

	/// <summary>
	///     Combines <see cref="BeatmapsetFolderName" /> with <see cref="StorageOptions.BeatmapsetsPath" /> to
	///     get the path a beatmapset is expected to live at.
	/// </summary>
	public static string BeatmapsetFolderPath(StorageOptions storage, Beatmapset beatmapset)
	{
		return Path.Combine(storage.BeatmapsetsPath, BeatmapsetFolderName(beatmapset));
	}

	/// <summary>
	///     Resolves a beatmapset's actual folder on disk by leading-id prefix rather than recomputing
	///     <see cref="BeatmapsetFolderPath" /> from the beatmapset's current (mutable) Artist/Title: those can
	///     drift from whatever the folder was actually named at ingestion time (e.g., a re-ingesting that
	///     revised the parsed title, or a folder that arrived pre-extracted with different
	///     illegal-character handling than <see cref="Sanitize" /> uses), which would otherwise make
	///     an existing, perfectly good folder invisible to every read path. Null if no folder with
	///     that leading id exists.
	/// </summary>
	public static string? FindBeatmapsetFolder(StorageOptions storage, int beatmapsetId)
	{
		return Directory.Exists(storage.BeatmapsetsPath)
			? Directory.EnumerateDirectories(storage.BeatmapsetsPath, $"{beatmapsetId} *").FirstOrDefault()
			: null;
	}

	/// <summary>
	///     Builds the conventional canonical-archive name for a beatmapset:
	///     <c>"{Id} {Artist} - {Title}.osz"</c>, with characters invalid in filenames stripped out.
	/// </summary>
	public static string BeatmapsetOszName(Beatmapset beatmapset)
	{
		return BeatmapsetFolderName(beatmapset) + ".osz";
	}

	/// <summary>
	///     Combines <see cref="BeatmapsetOszName" /> with <see cref="StorageOptions.BeatmapsetsPath" /> to
	///     get the path a beatmapset's canonical archive is expected to live at.
	/// </summary>
	public static string BeatmapsetOszPath(StorageOptions storage, Beatmapset beatmapset)
	{
		return Path.Combine(storage.BeatmapsetsPath, BeatmapsetOszName(beatmapset));
	}

	/// <summary>
	///     Resolves a beatmapset's canonical <c>.osz</c> on disk by leading-id prefix, the same way
	///     <see cref="FindBeatmapsetFolder" /> resolves the legacy extracted-folder layout. Null if no
	///     archive with that leading id exists — either the set hasn't been migrated from a legacy
	///     folder yet, or it doesn't exist at all.
	/// </summary>
	public static string? FindBeatmapsetOsz(StorageOptions storage, int beatmapsetId)
	{
		return Directory.Exists(storage.BeatmapsetsPath)
			? Directory.EnumerateFiles(storage.BeatmapsetsPath, $"{beatmapsetId} *.osz").FirstOrDefault()
			: null;
	}

	/// <summary>
	///     Resolves the path to a beatmap's `.osu` file on disk, or <see langword="null" /> when its
	///     beatmapset folder can't be found.
	/// </summary>
	public static string? OsuFilePath(StorageOptions storage, Beatmap beatmap)
	{
		var folder = FindBeatmapsetFolder(storage, beatmap.Beatmapset.Id);
		return folder is null ? null : Path.Combine(folder, beatmap.Filename);
	}

	/// <summary>Null if the beatmap has no recorded background filename or its beatmapset folder can't be found.</summary>
	public static string? BackgroundFilePath(StorageOptions storage, Beatmap beatmap)
	{
		if (beatmap.BackgroundFile is null) return null;
		var folder = FindBeatmapsetFolder(storage, beatmap.Beatmapset.Id);
		return folder is null ? null : Path.Combine(folder, beatmap.BackgroundFile);
	}

	/// <summary>Null if the beatmapset has no recorded preview background or its folder can't be found.</summary>
	public static string? BackgroundFilePath(StorageOptions storage, Beatmapset beatmapset)
	{
		if (beatmapset.BackgroundFile is null) return null;
		var folder = FindBeatmapsetFolder(storage, beatmapset.Id);
		return folder is null ? null : Path.Combine(folder, beatmapset.BackgroundFile);
	}

	/// <summary>Null if the beatmap has no recorded audio filename or its beatmapset folder can't be found.</summary>
	public static string? AudioFilePath(StorageOptions storage, Beatmap beatmap)
	{
		if (beatmap.AudioFile is null) return null;
		var folder = FindBeatmapsetFolder(storage, beatmap.Beatmapset.Id);
		return folder is null ? null : Path.Combine(folder, beatmap.AudioFile);
	}

	/// <summary>Null if the beatmapset has no recorded preview audio or its folder can't be found.</summary>
	public static string? AudioFilePath(StorageOptions storage, Beatmapset beatmapset)
	{
		if (beatmapset.AudioFile is null) return null;
		var folder = FindBeatmapsetFolder(storage, beatmapset.Id);
		return folder is null ? null : Path.Combine(folder, beatmapset.AudioFile);
	}

	/// <summary>
	///     Null if the beatmap's `.osu` declares no video in `[Events]`, its file is missing on disk, or
	///     its beatmapset folder can't be found. Unlike <see cref="BackgroundFilePath(StorageOptions,Beatmap)" />/
	///     <see cref="AudioFilePath(StorageOptions,Beatmap)" />, video has no ingestion-time DB column:
	///     the filename is re-derived from the `.osu`'s own <see cref="Storyboard" /> via
	///     <see cref="Storyboard.PrimaryVideo" /> on every call.
	/// </summary>
	public static string? VideoFilePath(StorageOptions storage, Beatmap beatmap)
	{
		var osuPath = OsuFilePath(storage, beatmap);
		if (osuPath is null || !File.Exists(osuPath)) return null;

		using var stream = File.OpenRead(osuPath);
		using var reader = new LineBufferedReader(stream);
		var storyboard = Decoder.GetDecoder<Storyboard>(reader).Decode(reader);
		var videoFilename = storyboard.PrimaryVideo?.Path;
		if (videoFilename is null) return null;

		var folder = FindBeatmapsetFolder(storage, beatmap.Beatmapset.Id);
		return folder is null ? null : Path.Combine(folder, videoFilename);
	}

	/// <summary>
	///     Full pass: reconciles every loose ".osz" at the storage root (the canonical layout) and
	///     every subfolder that looks like a beatmapset (".osu" files at depth 1 — the legacy,
	///     not-yet-migrated layout), then deletes any Beatmapset row present under neither layout.
	///     Returns the number of beatmaps added or updated this pass.
	/// </summary>
	public async Task<int> ReconcileAllAsync(CancellationToken cancellationToken = default)
	{
		var path = options.Value.BeatmapsetsPath;
		Directory.CreateDirectory(path);

		var ingested = 0;
		var seenSetIds = new HashSet<int>();

		// Snapshotted before iterating: ReconcileOszAsync below moves a loose .osz to its canonical
		// filename within this same directory (it no longer deletes it, per ADR-006), and a lazily
		// evaluated Directory.EnumerateFiles is not guaranteed safe against a rename of an
		// already-yielded entry mid-enumeration.
		foreach (var oszPath in Directory.EnumerateFiles(path, "*.osz").ToList())
		{
			var (count, setId) = await ReconcileOszAsync(oszPath, cancellationToken);
			ingested += count;
			if (setId is not null) seenSetIds.Add(setId.Value);
		}

		// A set not yet migrated to the canonical .osz layout (an existing deployment's pre-upgrade
		// data, or one the background migration pass hasn't reached yet) still lives as an extracted
		// folder. Reconciling it here — not just relying on the migration pass — keeps the orphan
		// sweep below from treating a not-yet-migrated set as gone: it is "seen" under whichever
		// layout it currently has, for the whole migration window, not only after migration completes.
		foreach (var folder in Directory.EnumerateDirectories(path).ToList())
		{
			if (Path.GetFileName(folder).Contains(DeletedFolderInfix, StringComparison.OrdinalIgnoreCase)) continue;

			var (count, setId) = await ReconcileFolderAsync(folder, cancellationToken);
			ingested += count;
			if (setId is not null) seenSetIds.Add(setId.Value);
		}

		// A loose .osu file has no set context on its own (no sibling assets, no folder name to
		// derive Artist/Title from); this pathway is deliberately not supported.
		foreach (var strayOsu in Directory.EnumerateFiles(path, "*.osu"))
			logger.LogWarning(
				"Ignoring stray .osu file at Beatmapsets root: {Path}. A single .osu has no set context — " +
				"drop a full .osz instead, or place it inside its own beatmapset folder.", strayOsu);

		var known = await beatmapsetRepository.FetchAllIdsAsync(cancellationToken);
		foreach (var orphanId in known.Where(id => !seenSetIds.Contains(id)))
			await DeleteBeatmapsetAsync(orphanId, cancellationToken);

		return ingested;
	}

	/// <summary>
	///     Decodes every ".osu" entry of a loose ".osz", resolves/creates its Beatmapset, moves the
	///     archive to its canonical filename (kept permanently — ADR-006's ".osz direct storage"
	///     design, no longer extracted into a folder and deleted), then upserts each decoded beatmap,
	///     eagerly extracting every ".osu" file plus the set's preview background/audio into
	///     <see cref="BeatmapsetAssetCache" /> as it goes (needed for analysis regardless, and for
	///     the difficulty-recompute route's arbitrary-time re-reads). An archive with no valid ".osu"
	///     content is deleted rather than kept, since it isn't a beatmapset.
	/// </summary>
	public async Task<(int Ingested, int? SetId)> ReconcileOszAsync(string oszPath,
		CancellationToken cancellationToken = default)
	{
		var decoded = new List<DecodedFile>();
		// .osu entries are conventionally at the archive root, but this maps each decoded file's
		// basename back to the entry's full in-archive name regardless, so BeatmapsetAssetCache's
		// lookup (matched against ZipArchiveEntry.FullName) is correct even for an archive that
		// nests them.
		var entryFullNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		await using (var archive = await ZipFile.OpenReadAsync(oszPath, cancellationToken))
		{
			foreach (var entry in archive.Entries)
			{
				if (!entry.Name.EndsWith(".osu", StringComparison.OrdinalIgnoreCase)) continue;

				await using var entryStream = await entry.OpenAsync(cancellationToken);
				using var buffer = new MemoryStream();
				await entryStream.CopyToAsync(buffer, cancellationToken);
				var osuBytes = buffer.ToArray();

				var parsed = TryDecode(osuBytes);
				if (parsed is null) continue;

				decoded.Add(new DecodedFile(entry.Name, osuCalculator.ComputeBeatmapMd5(osuBytes), parsed));
				entryFullNames[entry.Name] = entry.FullName;
			}
		}

		if (decoded.Count == 0)
		{
			File.Delete(oszPath);
			return (0, null);
		}

		var beatmapset =
			await ResolveBeatmapsetAsync(decoded, Path.GetFileNameWithoutExtension(oszPath), cancellationToken);

		var canonicalOszPath = BeatmapsetOszPath(options.Value, beatmapset);
		if (!string.Equals(Path.GetFullPath(oszPath), Path.GetFullPath(canonicalOszPath),
			    StringComparison.OrdinalIgnoreCase))
		{
			Directory.CreateDirectory(options.Value.BeatmapsetsPath);
			// A stale canonical archive at this id (e.g., a prior ingest's content this upload's
			// MD5/leading-id resolution matched onto) is replaced, not merged with.
			if (File.Exists(canonicalOszPath)) File.Delete(canonicalOszPath);
			File.Move(oszPath, canonicalOszPath);
		}

		var (ingested, previewBackground, previewAudio) = await UpsertDecodedBeatmapsAsync(decoded, beatmapset,
			async (file, ct) =>
			{
				var fullName = entryFullNames.GetValueOrDefault(file.OriginalFilename, file.OriginalFilename);
				return await assetCache.ResolveAsync(beatmapset.Id, fullName, canonicalOszPath, ct);
			}, cancellationToken);

		if (previewBackground is not null)
			await assetCache.ResolveAsync(beatmapset.Id, previewBackground, canonicalOszPath, cancellationToken);
		if (previewAudio is not null)
			await assetCache.ResolveAsync(beatmapset.Id, previewAudio, canonicalOszPath, cancellationToken);

		return (ingested, beatmapset.Id);
	}

	/// <summary>
	///     Extracts every entry of a `.osz` archive into <paramref name="targetFolder" /> (overwriting
	///     existing files), creating the folder if needed. Filesystem-only, never touches the
	///     database; the caller (or the live <see cref="BeatmapWatcherService" />) is responsible for
	///     any DB reconciliation that should follow.
	/// </summary>
	public static async Task ExtractOszIntoFolderAsync(string oszPath, string targetFolder,
		CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(targetFolder);

		await using var archive = await ZipFile.OpenReadAsync(oszPath, cancellationToken);
		foreach (var entry in archive.Entries)
		{
			if (entry.Name.Length == 0) continue; // directory entry

			var destination = Path.GetFullPath(Path.Combine(targetFolder, entry.FullName));
			if (!destination.StartsWith(targetFolder, StringComparison.OrdinalIgnoreCase))
				continue; // zip-slip guard

			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			await entry.ExtractToFileAsync(destination, true, cancellationToken);
		}
	}

	/// <summary>
	///     Adds/updates/deletes beatmaps for one beatmapset folder to match its current ".osu" files at
	///     depth 1. Returns 0 (and a null BeatmapsetId) when the folder has no ".osu" files at all: not a
	///     beatmapset folder.
	/// </summary>
	public async Task<(int Ingested, int? SetId)> ReconcileFolderAsync(string folderPath,
		CancellationToken cancellationToken = default)
	{
		var decoded = new List<DecodedFile>();
		foreach (var osuPath in Directory.EnumerateFiles(folderPath, "*.osu", SearchOption.TopDirectoryOnly))
		{
			var osuBytes = await File.ReadAllBytesAsync(osuPath, cancellationToken);
			var parsed = TryDecode(osuBytes);
			if (parsed is null) continue;

			decoded.Add(new DecodedFile(
				Path.GetFileName(osuPath), osuCalculator.ComputeBeatmapMd5(osuBytes), parsed));
		}

		if (decoded.Count == 0) return (0, null);

		var beatmapset = await ResolveBeatmapsetAsync(decoded, Path.GetFileName(folderPath), cancellationToken);
		var (ingested, _, _) = await UpsertDecodedBeatmapsAsync(decoded, beatmapset,
			(file, _) => Task.FromResult<string?>(Path.Combine(folderPath, file.OriginalFilename)), cancellationToken);

		return (ingested, beatmapset.Id);
	}

	/// <summary>
	///     Upserts every decoded ".osu" file against <paramref name="beatmapset" />, drops any
	///     previously-known beatmap no longer present, and records the resulting preview
	///     background/audio filenames on the set. Shared between the legacy folder-based reconcile
	///     path and the canonical archive-based one, which differ only in how a decoded file's real
	///     path for difficulty analysis is resolved (a direct folder-relative path for the former, an
	///     on-demand <see cref="BeatmapsetAssetCache" /> extraction for the latter).
	/// </summary>
	/// <param name="decoded">Every ".osu" file already decoded for this pass.</param>
	/// <param name="beatmapset">The beatmapset every decoded file belongs to.</param>
	/// <param name="resolveAnalysisPath">
	///     Resolves a decoded file's real path for <see cref="TryAnalyze" />, or <see langword="null" />
	///     when it can't be resolved. Called for every file regardless of whether re-analysis is
	///     actually needed, so a cache-backed resolver still gets the chance to ensure the file is
	///     extracted for later on-demand reads (e.g., the difficulty-recompute route).
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	private async Task<(int Ingested, string? PreviewBackground, string? PreviewAudio)> UpsertDecodedBeatmapsAsync(
		IReadOnlyList<DecodedFile> decoded, Beatmapset beatmapset,
		Func<DecodedFile, CancellationToken, Task<string?>> resolveAnalysisPath, CancellationToken cancellationToken)
	{
		var ingested = 0;

		foreach (var file in decoded)
		{
			var existingByPath = await beatmaps.FetchOneAsync(
				filename: file.OriginalFilename, setId: beatmapset.Id, includePrivate: true,
				cancellationToken: cancellationToken);

			var info = file.Parsed.BeatmapInfo;
			var mode = (GameMode)info.Ruleset.OnlineID;
			// Content unchanged (same md5) and already has a cached analysis (matches the "Sr > 0
			// means cached" convention /difficulty-rating already uses) -> skip recalculating on
			// every reconciling pass (server startup, watcher); otherwise compute it, which also
			// backfills any pre-existing row still sitting at the old default of 0/empty.
			var cacheHit = existingByPath is { Difficulty.Sr: > 0 } && existingByPath.Md5 == file.Md5;
			var analysisPath = await resolveAnalysisPath(file, cancellationToken);
			var analysis = cacheHit
				? new BeatmapAnalysis(existingByPath!.Difficulty, existingByPath.ObjectCounts)
				: analysisPath is null
					? EmptyAnalysis(mode)
					: TryAnalyze(analysisPath, mode);
			var backgroundFile = cacheHit ? existingByPath!.BackgroundFile : info.Metadata.BackgroundFile;
			var audioFile = cacheHit ? existingByPath!.AudioFile : info.Metadata.AudioFile;
			var previewTime = cacheHit ? existingByPath!.PreviewTime : info.Metadata.PreviewTime;

			var beatmap = new Beatmap(
				file.Md5,
				existingByPath?.Id ?? (info.OnlineID > 0 ? info.OnlineID : 0),
				beatmapset,
				info.DifficultyName,
				file.OriginalFilename,
				analysis.Difficulty,
				analysis.ObjectCounts,
				backgroundFile,
				audioFile,
				previewTime);

			await beatmaps.UpsertAsync(beatmap, cancellationToken);
			ingested++;

			if (existingByPath is null)
				logger.LogInformation("+ Beatmap ingested: {Filename} (Beatmapset {BeatmapsetId})",
					file.OriginalFilename,
					beatmapset.Id);
			else if (!cacheHit)
				logger.LogInformation("~ Beatmap updated: {Filename} (Beatmapset {BeatmapsetId})",
					file.OriginalFilename,
					beatmapset.Id);
		}

		var onDisk = decoded.Select(f => f.OriginalFilename).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var known = await beatmaps.FetchAllBySetIdAsync(beatmapset.Id, true, cancellationToken);
		foreach (var gone in known.Where(k => !onDisk.Contains(k.Filename)))
		{
			await beatmaps.DeleteByMd5Async(gone.Md5, cancellationToken);
			logger.LogInformation("- Beatmap removed: {Filename} (Beatmapset {BeatmapsetId})", gone.Filename,
				beatmapset.Id);
		}

		// decoded.Count > 0 (checked by both callers) guarantees at least one beatmap survives this
		// pass, so there's always a lowest-id one to derive the set's preview from.
		var remaining = known.Where(k => onDisk.Contains(k.Filename)).ToList();
		var preview = remaining.MinBy(b => b.Id);
		await beatmapsetRepository.SetBackgroundFileAsync(beatmapset.Id, preview?.BackgroundFile, cancellationToken);
		await beatmapsetRepository.SetAudioFileAsync(beatmapset.Id, preview?.AudioFile, cancellationToken);

		return (ingested, preview?.BackgroundFile, preview?.AudioFile);
	}

	/// <summary>
	///     A beatmapset folder vanished from the disk: drop its DB row (Beatmaps cascade via FK) if its
	///     leading id is still parseable and known.
	/// </summary>
	public async Task ReconcileDeletedFolderAsync(string folderPath, CancellationToken cancellationToken = default)
	{
		var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		var match = LeadingIdPattern.Match(name);
		if (!match.Success || !int.TryParse(match.Groups[1].Value, out var id)) return;

		if (await beatmapsetRepository.FetchByIdAsync(id, cancellationToken) is not null)
			await DeleteBeatmapsetAsync(id, cancellationToken);
		// A manually renamed-away-from-convention folder that's then deleted leaves an orphan row
		// until the next ReconcileAllAsync pass reclaims it. Acceptable for a human-admin server.
	}

	/// <summary>
	///     Drops the beatmapset's DB row (Beatmaps cascade via FK) and its resize/transcode cache
	///     entries (thumb small/large, audio preview; see <see cref="ResponseCacheKeys" />), so stale
	///     bytes can't be served for a beatmapset id that no longer exists. This is the single call
	///     site both <see cref="ReconcileAllAsync" />'s orphan sweep and
	///     <see cref="ReconcileDeletedFolderAsync" />'s watcher path go through, so cache invalidation
	///     can't be forgotten at one but not the other.
	/// </summary>
	private async Task DeleteBeatmapsetAsync(int id, CancellationToken cancellationToken)
	{
		await beatmapsetRepository.DeleteAsync(id, cancellationToken);
		await cache.DeleteAsync("thumb", ResponseCacheKeys.Thumb(id, false), cancellationToken);
		await cache.DeleteAsync("thumb", ResponseCacheKeys.Thumb(id, true), cancellationToken);
		await cache.DeleteAsync("preview", ResponseCacheKeys.Preview(id), cancellationToken);
		logger.LogInformation("- Beatmapset removed: {BeatmapsetId}", id);
	}

	/// <summary>
	///     Resolves which Beatmapset a batch of decoded .osu files belongs to: content-hash match against
	///     an existing Beatmap first (content identity wins over whatever the folder is currently
	///     named), else a leading id parsed from the folder/file name if that Beatmapset still exists,
	///     else a brand-new set (online BeatmapsetId if the client embedded one, else a fresh local id).
	/// </summary>
	private async Task<Beatmapset> ResolveBeatmapsetAsync(IReadOnlyList<DecodedFile> decoded, string folderOrFileName,
		CancellationToken cancellationToken)
	{
		foreach (var file in decoded)
		{
			var existing = await beatmaps.FetchOneAsync(md5: file.Md5, includePrivate: true,
				cancellationToken: cancellationToken);
			if (existing is not null) return existing.Beatmapset;
		}

		var match = LeadingIdPattern.Match(folderOrFileName);
		if (match.Success && int.TryParse(match.Groups[1].Value, out var leadingId))
		{
			var existingBeatmapset = await beatmapsetRepository.FetchByIdAsync(leadingId, cancellationToken);
			if (existingBeatmapset is not null)
				return await RefreshBeatmapsetAsync(existingBeatmapset, decoded[0], cancellationToken);
		}

		var onlineSetId = decoded[0].Parsed.BeatmapInfo.BeatmapSet?.OnlineID;
		var newId = onlineSetId is > 0
			? onlineSetId.Value
			: Math.Max(Beatmap.LocalIdFloor, await beatmapsetRepository.FetchMaxIdAsync(cancellationToken) + 1);

		return await RefreshBeatmapsetAsync(null, decoded[0], cancellationToken, newId);
	}

	/// <summary>
	///     Creates or updates the Beatmapset row for a set, deriving artist, title, and creator from the
	///     first decoded .osu file, preserving the original creation timestamp when an existing set is
	///     refreshed, and allocating <paramref name="newId" /> when creating a new one.
	/// </summary>
	private async Task<Beatmapset> RefreshBeatmapsetAsync(Beatmapset? existing, DecodedFile first,
		CancellationToken cancellationToken,
		int? newId = null)
	{
		var info = first.Parsed.BeatmapInfo;
		var now = DateTime.UtcNow;
		var beatmapset = new Beatmapset(
			existing?.Id ?? newId!.Value,
			info.Metadata.Artist,
			info.Metadata.Title,
			info.Metadata.Author.Username,
			now,
			existing?.CreatedAt ?? now);

		if (existing is null)
			logger.LogInformation("+ Beatmapset created: {BeatmapsetId} {Artist} - {Title}", beatmapset.Id,
				beatmapset.Artist,
				beatmapset.Title);

		return await beatmapsetRepository.UpsertAsync(beatmapset, cancellationToken);
	}

	/// <summary>
	///     Runs <see cref="IOsuCalculator.Analyze" /> for a beatmap, falling back to an all-zero
	///     difficulty and empty object counts when calculation fails.
	/// </summary>
	private BeatmapAnalysis TryAnalyze(string osuFilePath, GameMode mode)
	{
		try
		{
			return osuCalculator.Analyze(osuFilePath, mode, Mods.NoMod);
		}
		catch (Exception e)
		{
			// A map whose difficulty can't be calculated (unsupported ruleset content, corrupt
			// hitobjects) still gets ingested; it just keeps Sr at 0 and empty object counts instead
			// of aborting.
			logger.LogWarning(e, "Failed to analyze beatmap {Path}.", osuFilePath);
			return EmptyAnalysis(mode);
		}
	}

	/// <summary>
	///     An all-zero difficulty and empty object counts for <paramref name="mode" />, used both by
	///     <see cref="TryAnalyze" />'s failure fallback and when a decoded file's real path for
	///     analysis can't be resolved at all (e.g., an archive entry vanished between decode and
	///     extraction).
	/// </summary>
	private static BeatmapAnalysis EmptyAnalysis(GameMode mode)
	{
		var emptyDifficulty = new Difficulty(mode, 0, TimeSpan.Zero, 0, 0, 0, 0, 0);
		BeatmapObjectCounts emptyObjectCounts = mode switch
		{
			GameMode.Standard => new OsuBeatmapObjectCounts(),
			GameMode.Taiko => new TaikoBeatmapObjectCounts(),
			GameMode.Catch => new CatchBeatmapObjectCounts(),
			GameMode.Mania => new ManiaBeatmapObjectCounts(),
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ruleset for game mode.")
		};
		return new BeatmapAnalysis(emptyDifficulty, emptyObjectCounts);
	}

	private static LazerBeatmap? TryDecode(byte[] osuBytes)
	{
		try
		{
			using var stream = new MemoryStream(osuBytes);
			using var reader = new LineBufferedReader(stream);
			return Decoder.GetDecoder<LazerBeatmap>(reader).Decode(reader);
		}
		catch
		{
			// Skip malformed .osu files rather than aborting the whole scan.
			return null;
		}
	}

	/// <summary>Strips every character invalid in filenames from <paramref name="name" />.</summary>
	private static string Sanitize(string name)
	{
		return IllegalFilenameChars.Aggregate(name, (current, c) => current.Replace(c.ToString(), ""));
	}

	[GeneratedRegex(@"^(\d+)")]
	private static partial Regex LeadingIdRegex();

	private sealed record DecodedFile(string OriginalFilename, string Md5, LazerBeatmap Parsed);
}