using System.IO.Compression;
using System.Text.RegularExpressions;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configurations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>
///     One-time background conversion of a legacy extracted-folder beatmapset (the pre-ADR-006
///     layout) to the canonical loose ".osz" layout (ADR-006's ".osz direct storage" design): builds
///     the archive from the folder's contents, then moves the folder's already-extracted files
///     directly into <see cref="BeatmapsetAssetCache" /> as a free pre-warm rather than re-extracting
///     them from the archive just built.
/// </summary>
/// <remarks>
///     Runs once at startup as a background task and is never awaited by app startup: a real
///     library's worth of sets can take minutes to convert (ADR-006's measured cost), so this must
///     never delay the app accepting connections. On .NET 10, <see cref="BackgroundService" /> always
///     runs the whole of <see cref="ExecuteAsync" /> on a background thread rather than the pre-.NET
///     10 behavior of running its synchronous portion inline during host startup, so this holds even
///     when every set turns out to be a cheap synchronous skip (nothing left to convert).
///     <see cref="BeatmapIngestionService.ReconcileAllAsync" /> already runs synchronously before this
///     service starts, so a set not yet reached by this pass is still recognized as live under its
///     legacy folder for the whole conversion window; a set already migrated (its canonical ".osz"
///     already exists) is skipped cheaply without touching its folder.
/// </remarks>
public sealed partial class BeatmapsetMigrationService(
	IBeatmapsetRepository beatmapsets,
	IOptions<StorageOptions> options,
	BeatmapsetAssetCache assetCache,
	ILogger<BeatmapsetMigrationService> logger) : BackgroundService
{
	/// <summary>Runs the one-time conversion pass over every legacy folder present at startup.</summary>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var path = options.Value.BeatmapsetsPath;
		if (!Directory.Exists(path)) return;

		var migrated = 0;
		// Snapshotted before iterating: a successful migration renames/removes the folder being
		// enumerated, which a lazily evaluated Directory.EnumerateDirectories is not guaranteed safe
		// against.
		foreach (var folder in Directory.EnumerateDirectories(path).ToList())
		{
			if (stoppingToken.IsCancellationRequested) break;
			if (Path.GetFileName(folder)
			    .Contains(BeatmapIngestionService.DeletedFolderInfix, StringComparison.OrdinalIgnoreCase))
				continue;

			try
			{
				if (await MigrateOneAsync(folder, stoppingToken)) migrated++;
			}
			catch (OperationCanceledException)
			{
				// expected on shutdown
			}
			catch (Exception e)
			{
				// One set's failure (e.g. a locked file) doesn't abort the whole pass; it simply
				// stays on the legacy layout until the next server restart retries it.
				logger.LogWarning(e,
					"Failed to migrate legacy beatmapset folder {Path} to the canonical .osz layout; it will " +
					"remain on the legacy layout until a later attempt.", folder);
			}
		}

		if (migrated > 0)
			logger.LogInformation(
				"Beatmapset migration pass complete: {Count} set(s) converted to the canonical .osz layout.",
				migrated);
	}

	/// <summary>
	///     Converts one legacy folder, or does nothing and returns <see langword="false" /> when it
	///     isn't a recognizable, not-yet-migrated beatmapset folder.
	/// </summary>
	private async Task<bool> MigrateOneAsync(string folder, CancellationToken cancellationToken)
	{
		var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		var match = LeadingIdRegex().Match(name);
		if (!match.Success || !int.TryParse(match.Groups[1].Value, out var id))
		{
			logger.LogWarning("Skipping legacy beatmapset folder with no parseable leading id: {Path}", folder);
			return false;
		}

		// Cheap skip check (~17.5us/set measured against real data): already migrated, nothing to do.
		if (BeatmapIngestionService.FindBeatmapsetOsz(options.Value, id) is not null) return false;

		// A folder with no ".osu" file at all isn't a beatmapset (the same definition
		// BeatmapIngestionService.ReconcileFolderAsync already uses). Zipping it anyway would hand the
		// live watcher a canonical ".osz" whose own ReconcileOszAsync pass immediately deletes it as
		// unparseable, wasting the zip and racing this pass's asset-cache pre-warm for nothing.
		if (Directory.EnumerateFiles(folder, "*.osu", SearchOption.TopDirectoryOnly).FirstOrDefault() is null)
			return false;

		var beatmapset = await beatmapsets.FetchByIdAsync(id, cancellationToken);
		if (beatmapset is null)
		{
			// InitializeDataAsync's synchronous ReconcileAllAsync pass always runs before this
			// background service starts, so every legacy folder should already have a matching DB
			// row by now; a miss here means the folder was altered out from under this pass mid-run.
			logger.LogWarning("Skipping legacy beatmapset folder with no matching DB row (id {Id}): {Path}", id,
				folder);
			return false;
		}

		var canonicalOszPath = BeatmapIngestionService.BeatmapsetOszPath(options.Value, beatmapset);
		// Temp-then-rename (matches BeatmapsetAssetCache/FileSystemResponseCache's own write pattern):
		// a crash mid-build never leaves a half-written archive at a name FindBeatmapsetOsz would
		// match, so a retried pass rebuilds cleanly instead of treating a partial archive as done.
		var tempOszPath = canonicalOszPath + ".tmp";
		if (File.Exists(tempOszPath)) File.Delete(tempOszPath);
		await ZipFile.CreateFromDirectoryAsync(folder, tempOszPath, CompressionLevel.Fastest, false,
			cancellationToken);
		File.Move(tempOszPath, canonicalOszPath, true);

		// Free pre-warm: the folder's contents already match what eager population would produce, so
		// move them into the asset cache directly instead of re-extracting from the archive just
		// built. Directory.Move is an O(1) rename when both paths share a volume (confirmed
		// ~0.5ms/set against real data); StorageOptions lets BeatmapsetsPath and CachePath be
		// configured on different volumes, where Directory.Move throws IOException -- fall back to a
		// real copy+delete rather than silently taking the slow path while claiming a rename.
		var cacheSetDir = assetCache.SetDirectoryFor(id);
		if (Directory.Exists(cacheSetDir))
		{
			// Something already populated this id's cache directory; the archive above is already
			// canonical either way, so just drop the now-redundant legacy folder.
			Directory.Delete(folder, true);
			return true;
		}

		try
		{
			Directory.Move(folder, cacheSetDir);
		}
		catch (IOException)
		{
			logger.LogWarning(
				"Asset cache pre-warm for beatmapset {Id} could not use a fast rename (BeatmapsetsPath and " +
				"CachePath are on different volumes); falling back to a slower copy.", id);
			CopyDirectoryContents(folder, cacheSetDir);
			Directory.Delete(folder, true);
		}

		return true;
	}

	private static void CopyDirectoryContents(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(source, file);
			var target = Path.Combine(destination, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target, true);
		}
	}

	[GeneratedRegex(@"^(\d+)")]
	private static partial Regex LeadingIdRegex();
}