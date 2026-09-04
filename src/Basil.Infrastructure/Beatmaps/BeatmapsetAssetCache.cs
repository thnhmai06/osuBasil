using System.Collections.Concurrent;
using System.IO.Compression;
using Basil.Application.Configurations;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>
///     Resolves an individual file inside a beatmapset's canonical <c>.osz</c> archive to a real
///     path on disk, extracting it on a cache miss.
/// </summary>
/// <remarks>
///     Every current asset consumer (ImageSharp.Web's file resolver, ffmpeg's file-path argument,
///     <c>IOsuCalculator.Analyze</c>) needs a real file path, not bytes, so this is a distinct
///     abstraction from <see cref="Application.Abstractions.Storage.IResponseCache" />, which
///     stores already-derived response bytes. Entries live under
///     <c>{StorageOptions.CachePath}/beatmapset-assets/{beatmapsetId}/{entryName}</c>, one
///     subdirectory per beatmapset, so a whole set's cache can be invalidated in a single
///     directory delete (<see cref="Invalidate" />). Writes use the same temp-file-then-rename
///     mechanism as <c>FileSystemResponseCache</c> (ADR-006), and a per-<c>(beatmapsetId,
///     entryName)</c> lock ensures concurrent misses for the same entry extract exactly once.
/// </remarks>
public sealed class BeatmapsetAssetCache(IOptions<StorageOptions> options)
{
	private const string CacheSubdirectory = "beatmapset-assets";

	/// <summary>Per-(beatmapset, entry) single-flight lock guarding <see cref="ResolveAsync" />'s extraction.</summary>
	private static readonly ConcurrentDictionary<(int BeatmapsetId, string EntryName), SemaphoreSlim> ExtractionLocks =
		new();

	/// <summary>
	///     Resolves <paramref name="entryName" />'s real path for <paramref name="beatmapsetId" />,
	///     extracting it from the archive at <paramref name="oszPath" /> on a cache miss.
	/// </summary>
	/// <param name="beatmapsetId">The beatmapset the entry belongs to.</param>
	/// <param name="entryName">The entry's name within the archive, matched case-insensitively.</param>
	/// <param name="oszPath">The path to the beatmapset's canonical <c>.osz</c> archive.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     The resolved absolute path, or <see langword="null" /> when the archive has no entry
	///     named <paramref name="entryName" />.
	/// </returns>
	public async Task<string?> ResolveAsync(int beatmapsetId, string entryName, string oszPath,
		CancellationToken cancellationToken = default)
	{
		var path = PathFor(beatmapsetId, entryName);
		if (File.Exists(path)) return path;

		var key = (beatmapsetId, entryName);
		var extractLock = ExtractionLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
		await extractLock.WaitAsync(cancellationToken);
		try
		{
			// Re-check now that this call owns the lock: a concurrent request may have already
			// populated the entry while this one was waiting.
			if (File.Exists(path)) return path;

			await using var archive = await ZipFile.OpenReadAsync(oszPath, cancellationToken);
			var entry = archive.Entries.FirstOrDefault(e =>
				string.Equals(e.FullName, entryName, StringComparison.OrdinalIgnoreCase));
			if (entry is null) return null;

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
			await using (var entryStream = await entry.OpenAsync(cancellationToken))
			await using (var fileStream = File.Create(tempPath))
			{
				await entryStream.CopyToAsync(fileStream, cancellationToken);
			}

			File.Move(tempPath, path, true);
			return path;
		}
		finally
		{
			extractLock.Release();
			// Every (beatmapsetId, entryName) pair is a distinct key with no natural end-of-life
			// event to remove it on, so leaving the entry behind would grow this dictionary without
			// bound for the server's whole lifetime. Compare-and-remove (not a plain key removal) so
			// a concurrent request that already looked up this exact SemaphoreSlim instance is never
			// affected by it being dropped here.
			ExtractionLocks.TryRemove(
				new KeyValuePair<(int BeatmapsetId, string EntryName), SemaphoreSlim>(key, extractLock));
		}
	}

	/// <summary>Removes every cached entry for <paramref name="beatmapsetId" /> in one directory delete.</summary>
	/// <remarks>Removing a beatmapset with nothing cached is a no-op.</remarks>
	public void Invalidate(int beatmapsetId)
	{
		var dir = SetDirectory(beatmapsetId);
		if (Directory.Exists(dir)) Directory.Delete(dir, true);
	}

	/// <summary>
	///     The directory <paramref name="beatmapsetId" />'s cached entries live under. Exposed for
	///     <see cref="BeatmapsetMigrationService" />, which pre-warms this cache by moving a legacy
	///     extracted folder's contents here directly instead of re-extracting them through
	///     <see cref="ResolveAsync" />.
	/// </summary>
	public string SetDirectoryFor(int beatmapsetId)
	{
		return SetDirectory(beatmapsetId);
	}

	/// <summary>Builds the absolute cache path for one beatmapset's asset entry.</summary>
	private string PathFor(int beatmapsetId, string entryName)
	{
		var setDir = SetDirectory(beatmapsetId);
		// entryName is drawn from an uploaded .osz's own metadata (an .osu's declared
		// AudioFilename/background), so it is attacker-influenced content, not a trusted literal --
		// the same zip-slip guard ExtractOszIntoFolderAsync already applies to a full-archive
		// extraction applies here to a single named entry.
		var path = Path.GetFullPath(Path.Combine(setDir, entryName));
		if (!path.StartsWith(setDir, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException($"Entry name '{entryName}' resolves outside its cache directory.");
		return path;
	}

	private string SetDirectory(int beatmapsetId)
	{
		return Path.Combine(options.Value.CachePath, CacheSubdirectory, beatmapsetId.ToString());
	}
}