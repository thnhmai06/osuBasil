using System.IO.Compression;
using System.Text.RegularExpressions;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Storage;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Infrastructure.Performance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Storyboards;
using Beatmap = Basil.Domain.Beatmaps.Beatmap;
using LazerBeatmap = osu.Game.Beatmaps.Beatmap;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>
///     Keeps the DB in sync with <see cref="StorageOptions.MapsetsPath" />: one subfolder per
///     mapset (<c>"{MapsetId} {Artist} - {Title}"</c>, holding the full original .osz contents,
///     audio/images/backgrounds/video/.osu, as extracted, not renamed), or a loose ".osz" at the
///     root waiting to be extracted. A single loose ".osu" file has no set context on its own and
///     is never ingested. Uses ppy.osu.Game's own decoder (the same one
///     <see cref="PpyOsuCalculator" /> uses) so metadata parsing matches the real client
///     byte-for-byte. <see cref="BeatmapWatcherService" /> is the incremental caller (per-folder
///     reconciliation on every filesystem change); <see cref="ReconcileAllAsync" /> is the full-pass
///     caller.
/// </summary>
public sealed partial class BeatmapIngestionService(
	IBeatmapRepository beatmaps,
	IMapsetRepository mapsets,
	IOsuCalculator osuCalculator,
	IOptions<StorageOptions> options,
	IResponseCache cache,
	ILogger<BeatmapIngestionService> logger)
{
	/// <summary>
	///     Marker infix for a mapset folder mid-deletion (see <see cref="ReconcileDeletedFolderAsync" />'s
	///     doc comment): a folder whose name carries this infix is never treated as a live mapset.
	/// </summary>
	public const string DeletedFolderInfix = ".deleted_";

	// Platform-provided, not a hardcoded Windows list — on Linux (a supported deployment target, see
	// CLAUDE.md's win-x64/linux-x64 publish commands) this is just { '\0', '/' }, so e.g. "|" in a
	// beatmap's title is a perfectly valid path character there and is left alone.
	private static readonly char[] IllegalFilenameChars = Path.GetInvalidFileNameChars();
	private static readonly Regex LeadingIdPattern = LeadingIdRegex();

	/// <summary>
	///     Builds the conventional folder name for a mapset: <c>"{Id} {Artist} - {Title}"</c>, with
	///     characters invalid in filenames stripped out.
	/// </summary>
	public static string MapsetFolderName(Mapset mapset)
	{
		return Sanitize($"{mapset.Id} {mapset.Artist} - {mapset.Title}");
	}

	/// <summary>
	///     Combines <see cref="MapsetFolderName" /> with <see cref="StorageOptions.MapsetsPath" /> to
	///     get the path a mapset is expected to live at.
	/// </summary>
	public static string MapsetFolderPath(StorageOptions storage, Mapset mapset)
	{
		return Path.Combine(storage.MapsetsPath, MapsetFolderName(mapset));
	}

	/// <summary>
	///     Resolves a mapset's actual folder on disk by leading-id prefix rather than recomputing
	///     <see cref="MapsetFolderPath" /> from the mapset's current (mutable) Artist/Title: those can
	///     drift from whatever the folder was actually named at ingestion time (e.g. a re-ingest that
	///     revised the parsed title, or a folder that arrived pre-extracted with different
	///     illegal-character handling than <see cref="Sanitize" /> uses), which would otherwise make
	///     an existing, perfectly good folder invisible to every read path. Null if no folder with
	///     that leading id exists.
	/// </summary>
	public static string? FindMapsetFolder(StorageOptions storage, int mapsetId)
	{
		if (!Directory.Exists(storage.MapsetsPath)) return null;
		return Directory.EnumerateDirectories(storage.MapsetsPath, $"{mapsetId} *").FirstOrDefault();
	}

	/// <summary>
	///     Resolves the path to a beatmap's `.osu` file on disk, or <see langword="null" /> when its
	///     mapset folder can't be found.
	/// </summary>
	public static string? OsuFilePath(StorageOptions storage, Beatmap beatmap)
	{
		var folder = FindMapsetFolder(storage, beatmap.Mapset.Id);
		return folder is null ? null : Path.Combine(folder, beatmap.Filename);
	}

	/// <summary>Null if the beatmap has no recorded background filename or its mapset folder can't be found.</summary>
	public static string? BackgroundFilePath(StorageOptions storage, Beatmap beatmap)
	{
		if (beatmap.BackgroundFile is null) return null;
		var folder = FindMapsetFolder(storage, beatmap.Mapset.Id);
		return folder is null ? null : Path.Combine(folder, beatmap.BackgroundFile);
	}

	/// <summary>Null if the mapset has no recorded preview background or its folder can't be found.</summary>
	public static string? BackgroundFilePath(StorageOptions storage, Mapset mapset)
	{
		if (mapset.BackgroundFile is null) return null;
		var folder = FindMapsetFolder(storage, mapset.Id);
		return folder is null ? null : Path.Combine(folder, mapset.BackgroundFile);
	}

	/// <summary>Null if the beatmap has no recorded audio filename or its mapset folder can't be found.</summary>
	public static string? AudioFilePath(StorageOptions storage, Beatmap beatmap)
	{
		if (beatmap.AudioFile is null) return null;
		var folder = FindMapsetFolder(storage, beatmap.Mapset.Id);
		return folder is null ? null : Path.Combine(folder, beatmap.AudioFile);
	}

	/// <summary>Null if the mapset has no recorded preview audio or its folder can't be found.</summary>
	public static string? AudioFilePath(StorageOptions storage, Mapset mapset)
	{
		if (mapset.AudioFile is null) return null;
		var folder = FindMapsetFolder(storage, mapset.Id);
		return folder is null ? null : Path.Combine(folder, mapset.AudioFile);
	}

	/// <summary>
	///     Null if the beatmap's `.osu` declares no video in `[Events]`, its file is missing on disk, or
	///     its mapset folder can't be found. Unlike <see cref="BackgroundFilePath(StorageOptions,Beatmap)" />/
	///     <see cref="AudioFilePath(StorageOptions,Beatmap)" />, video has no ingestion-time DB column:
	///     the filename is re-derived from the `.osu`'s own <see cref="Storyboard" /> (osu.Game's own
	///     module, the same one background-finding effectively relies on) via
	///     <see cref="Storyboard.PrimaryVideo" /> on every call, since a request for it is rare enough
	///     that a stored column isn't worth the schema churn.
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

		var folder = FindMapsetFolder(storage, beatmap.Mapset.Id);
		return folder is null ? null : Path.Combine(folder, videoFilename);
	}

	/// <summary>
	///     Full pass: extracts every loose ".osz" at the storage root, reconciles every subfolder that
	///     looks like a mapset (".osu" files at depth 1), then deletes any Mapset row whose folder no
	///     longer exists on disk. Returns the number of beatmaps added or updated this pass.
	/// </summary>
	public async Task<int> ReconcileAllAsync(CancellationToken cancellationToken = default)
	{
		var path = options.Value.MapsetsPath;
		Directory.CreateDirectory(path);

		var ingested = 0;
		var seenSetIds = new HashSet<int>();
		// Folders present before osz extraction — a folder extracted by ReconcileOszAsync below
		// is already reconciled internally, so the second loop must skip it to avoid double-counting.
		var preExistingFolders = Directory.EnumerateDirectories(path).ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (var oszPath in Directory.EnumerateFiles(path, "*.osz"))
		{
			var (count, setId) = await ReconcileOszAsync(oszPath, cancellationToken);
			ingested += count;
			if (setId is not null) seenSetIds.Add(setId.Value);
		}

		foreach (var folder in Directory.EnumerateDirectories(path))
		{
			if (!preExistingFolders.Contains(folder)) continue;
			if (Path.GetFileName(folder).Contains(DeletedFolderInfix, StringComparison.OrdinalIgnoreCase)) continue;

			var (count, setId) = await ReconcileFolderAsync(folder, cancellationToken);
			ingested += count;
			if (setId is not null) seenSetIds.Add(setId.Value);
		}

		// A loose .osu file has no set context on its own (no sibling assets, no folder name to
		// derive Artist/Title from) — this pathway is deliberately not supported.
		foreach (var strayOsu in Directory.EnumerateFiles(path, "*.osu"))
			logger.LogWarning(
				"Ignoring stray .osu file at Mapsets root: {Path}. A single .osu has no set context — " +
				"drop a full .osz instead, or place it inside its own mapset folder.", strayOsu);

		var known = await mapsets.FetchAllIdsAsync(cancellationToken);
		foreach (var orphanId in known.Where(id => !seenSetIds.Contains(id)))
			await DeleteMapsetAsync(orphanId, cancellationToken);

		return ingested;
	}

	/// <summary>
	///     Extracts every entry of a loose ".osz" (audio/images/video/.osu, as-is) into its resolved mapset folder, then
	///     reconciles that folder.
	/// </summary>
	public async Task<(int Ingested, int? SetId)> ReconcileOszAsync(string oszPath,
		CancellationToken cancellationToken = default)
	{
		var decoded = new List<DecodedFile>();
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

				decoded.Add(new DecodedFile(entry.Name, osuBytes, osuCalculator.ComputeBeatmapMd5(osuBytes), parsed));
			}
		}

		if (decoded.Count == 0)
		{
			File.Delete(oszPath);
			return (0, null);
		}

		var mapset = await ResolveMapsetAsync(decoded, Path.GetFileNameWithoutExtension(oszPath), cancellationToken);
		var targetFolder = MapsetFolderPath(options.Value, mapset);
		await ExtractOszIntoFolderAsync(oszPath, targetFolder, cancellationToken);

		File.Delete(oszPath);

		var (count, _) = await ReconcileFolderAsync(targetFolder, cancellationToken);
		return (count, mapset.Id);
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
	///     Adds/updates/deletes beatmaps for one mapset folder to match its current ".osu" files at
	///     depth 1. Returns 0 (and a null MapsetId) when the folder has no ".osu" files at all: not a
	///     mapset folder.
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

			decoded.Add(new DecodedFile(Path.GetFileName(osuPath), osuBytes, osuCalculator.ComputeBeatmapMd5(osuBytes),
				parsed));
		}

		if (decoded.Count == 0) return (0, null);

		var mapset = await ResolveMapsetAsync(decoded, Path.GetFileName(folderPath), cancellationToken);
		var ingested = 0;

		foreach (var file in decoded)
		{
			var existingByPath = await beatmaps.FetchOneAsync(
				filename: file.OriginalFilename, setId: mapset.Id, includePrivate: true,
				cancellationToken: cancellationToken);

			var info = file.Parsed.BeatmapInfo;
			var mode = (GameMode)info.Ruleset.OnlineID;
			// Content unchanged (same md5) and already has a cached analysis (matches the "Sr > 0
			// means cached" convention /difficulty-rating already uses) -> skip recalculating on
			// every reconcile pass (server startup, watcher); otherwise compute it, which also
			// backfills any pre-existing row still sitting at the old default of 0/empty.
			var cacheHit = existingByPath is { Difficulty.Sr: > 0 } existing && existing.Md5 == file.Md5;
			var analysis = cacheHit
				? new BeatmapAnalysis(existingByPath!.Difficulty, existingByPath.BeatmapObjectCounts)
				: TryAnalyze(Path.Combine(folderPath, file.OriginalFilename), mode);
			var backgroundFile = cacheHit ? existingByPath!.BackgroundFile : info.Metadata.BackgroundFile;
			var audioFile = cacheHit ? existingByPath!.AudioFile : info.Metadata.AudioFile;
			var previewTime = cacheHit ? existingByPath!.PreviewTime : info.Metadata.PreviewTime;

			var beatmap = new Beatmap(
				file.Md5,
				existingByPath?.Id ?? (info.OnlineID > 0 ? info.OnlineID : 0),
				mapset,
				info.DifficultyName,
				file.OriginalFilename,
				analysis.Difficulty,
				analysis.BeatmapObjectCounts,
				backgroundFile,
				audioFile,
				previewTime);

			await beatmaps.UpsertAsync(beatmap, cancellationToken);
			ingested++;

			if (existingByPath is null)
				logger.LogInformation("+ Beatmap ingested: {Filename} (Mapset {MapsetId})", file.OriginalFilename,
					mapset.Id);
			else if (!cacheHit)
				logger.LogInformation("~ Beatmap updated: {Filename} (Mapset {MapsetId})", file.OriginalFilename,
					mapset.Id);
		}

		var onDisk = decoded.Select(f => f.OriginalFilename).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var known = await beatmaps.FetchAllBySetIdAsync(mapset.Id, true, cancellationToken);
		foreach (var gone in known.Where(k => !onDisk.Contains(k.Filename)))
		{
			await beatmaps.DeleteByMd5Async(gone.Md5, cancellationToken);
			logger.LogInformation("- Beatmap removed: {Filename} (Mapset {MapsetId})", gone.Filename, mapset.Id);
		}

		// decoded.Count > 0 (checked above) guarantees at least one beatmap survives this pass, so
		// there's always a lowest-id one to derive the set's preview from.
		var remaining = known.Where(k => onDisk.Contains(k.Filename)).ToList();
		var preview = remaining.MinBy(b => b.Id);
		await mapsets.SetBackgroundFileAsync(mapset.Id, preview?.BackgroundFile, cancellationToken);
		await mapsets.SetAudioFileAsync(mapset.Id, preview?.AudioFile, cancellationToken);

		return (ingested, mapset.Id);
	}

	/// <summary>
	///     A mapset folder vanished from disk: drop its DB row (Beatmaps cascade via FK) if its
	///     leading id is still parseable and known.
	/// </summary>
	public async Task ReconcileDeletedFolderAsync(string folderPath, CancellationToken cancellationToken = default)
	{
		var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		var match = LeadingIdPattern.Match(name);
		if (!match.Success || !int.TryParse(match.Groups[1].Value, out var id)) return;

		if (await mapsets.FetchByIdAsync(id, cancellationToken) is not null)
			await DeleteMapsetAsync(id, cancellationToken);
		// A manually-renamed-away-from-convention folder that's then deleted leaves an orphan row
		// until the next ReconcileAllAsync pass reclaims it — acceptable for a human-admin server.
	}

	/// <summary>
	///     Drops the mapset's DB row (Beatmaps cascade via FK) and its resize/transcode cache entries
	///     (thumb small/large, audio preview; see <see cref="ResponseCacheKeys" />), which would
	///     otherwise keep serving stale bytes for a mapset id that has since been reused or is simply
	///     gone. This is the single call site both <see cref="ReconcileAllAsync" />'s orphan sweep and
	///     <see cref="ReconcileDeletedFolderAsync" />'s watcher path go through, so cache invalidation
	///     can't be forgotten at one but not the other.
	/// </summary>
	private async Task DeleteMapsetAsync(int id, CancellationToken cancellationToken)
	{
		await mapsets.DeleteAsync(id, cancellationToken);
		await cache.DeleteAsync("thumb", ResponseCacheKeys.Thumb(id, false), cancellationToken);
		await cache.DeleteAsync("thumb", ResponseCacheKeys.Thumb(id, true), cancellationToken);
		await cache.DeleteAsync("preview", ResponseCacheKeys.Preview(id), cancellationToken);
		logger.LogInformation("- Mapset removed: {MapsetId}", id);
	}

	/// <summary>
	///     Resolves which Mapset a batch of decoded .osu files belongs to: content-hash match against
	///     an existing Beatmap first (content identity wins over whatever the folder is currently
	///     named), else a leading id parsed from the folder/file name if that Mapset still exists,
	///     else a brand-new set (online MapsetId if the client embedded one, else a fresh local id).
	/// </summary>
	private async Task<Mapset> ResolveMapsetAsync(IReadOnlyList<DecodedFile> decoded, string folderOrFileName,
		CancellationToken cancellationToken)
	{
		foreach (var file in decoded)
		{
			var existing = await beatmaps.FetchOneAsync(md5: file.Md5, includePrivate: true,
				cancellationToken: cancellationToken);
			if (existing is not null) return existing.Mapset;
		}

		var match = LeadingIdPattern.Match(folderOrFileName);
		if (match.Success && int.TryParse(match.Groups[1].Value, out var leadingId))
		{
			var existingMapset = await mapsets.FetchByIdAsync(leadingId, cancellationToken);
			if (existingMapset is not null)
				return await RefreshMapsetAsync(existingMapset, decoded[0], cancellationToken);
		}

		var onlineSetId = decoded[0].Parsed.BeatmapInfo.BeatmapSet?.OnlineID;
		var newId = onlineSetId is > 0
			? onlineSetId.Value
			: Math.Max(Beatmap.LocalIdFloor, await mapsets.FetchMaxIdAsync(cancellationToken) + 1);

		return await RefreshMapsetAsync(null, decoded[0], cancellationToken, newId);
	}

	/// <summary>
	///     Creates or updates the Mapset row for a set, deriving artist, title, and creator from the
	///     first decoded .osu file, preserving the original creation timestamp when an existing set is
	///     refreshed, and allocating <paramref name="newId" /> when creating a new one.
	/// </summary>
	private async Task<Mapset> RefreshMapsetAsync(Mapset? existing, DecodedFile first,
		CancellationToken cancellationToken,
		int? newId = null)
	{
		var info = first.Parsed.BeatmapInfo;
		var now = DateTime.UtcNow;
		var mapset = new Mapset(
			existing?.Id ?? newId!.Value,
			info.Metadata.Artist,
			info.Metadata.Title,
			info.Metadata.Author.Username,
			now,
			existing?.CreatedAt ?? now);

		if (existing is null)
			logger.LogInformation("+ Mapset created: {MapsetId} {Artist} - {Title}", mapset.Id, mapset.Artist,
				mapset.Title);

		return await mapsets.UpsertAsync(mapset, cancellationToken);
	}

	/// <summary>
	///     Runs <see cref="IOsuCalculator.Analyze" /> for a beatmap, falling back to an all-zero
	///     difficulty and empty object counts when calculation fails so the beatmap still ingests.
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
			// hitobjects) still gets ingested — it just keeps Sr at 0/no object counts instead of
			// aborting.
			logger.LogWarning(e, "Failed to analyze beatmap {Path}.", osuFilePath);
			var emptyDifficulty = new Difficulty(mode, 0, TimeSpan.Zero, 0, 0, 0, 0, 0);
			BeatmapObjectCounts emptyBeatmapObjectCounts = mode switch
			{
				GameMode.Standard => new OsuBeatmapObjectCounts(),
				GameMode.Taiko => new TaikoBeatmapObjectCounts(),
				GameMode.Catch => new CatchBeatmapObjectCounts(),
				GameMode.Mania => new ManiaBeatmapObjectCounts(),
				_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ruleset for game mode.")
			};
			return new BeatmapAnalysis(emptyDifficulty, emptyBeatmapObjectCounts);
		}
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

	private sealed record DecodedFile(string OriginalFilename, byte[] Bytes, string Md5, LazerBeatmap Parsed);
}