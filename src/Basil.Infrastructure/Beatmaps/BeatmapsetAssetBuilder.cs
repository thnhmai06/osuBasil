using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.IO.Compression;
using Basil.Application.Shared.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Shared.Media;
using Basil.Infrastructure.Shared.Storage;
using Basil.Application.Beatmaps;
using Microsoft.Extensions.Options;
using BeatmapIngestionService = Basil.Infrastructure.Beatmaps.BeatmapIngestionService;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>A dedicated logger category marker for <see cref="BeatmapsetAssetBuilder" />.</summary>
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class BeatmapsetAssetBuilderLog;

/// <summary>
///     Builds the derived, on-demand assets served for a beatmapset: a temporary download archive
///     and its short audio preview clip.
/// </summary>
public static class BeatmapsetAssetBuilder
{
	private static readonly FrozenSet<string> VideoExtensions =
		new[] { ".avi", ".flv", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".m4v", ".webm", ".wmv" }.ToFrozenSet(
			StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Creates a temporary <c>.osz</c> archive from a locally stored beatmapset.
	/// </summary>
	/// <remarks>
	///     <para>
	///         The archive contains the beatmapset's stored contents, including beatmaps, audio,
	///         images, and any other imported assets.
	///     </para>
	///     <para>
	///         When <paramref name="noVideo" /> is <see langword="true" />, video files are excluded
	///         and <c>" [no video]"</c> is appended to the returned filename.
	///     </para>
	/// </remarks>
	/// <returns>
	///     A generated <c>.osz</c> archive, or <see langword="null" /> if the beatmapset has no
	///     stored files.
	/// </returns>
	public static async Task<(byte[] Bytes, string FileName)?> BuildBeatmapsetArchiveAsync(
		IBeatmapRepository beatmapRepository,
		StorageOptions storage, int setId, bool noVideo, CancellationToken cancellationToken)
	{
		var beatmaps = await beatmapRepository.FetchAllBySetIdAsync(setId, cancellationToken: cancellationToken);
		if (beatmaps.Count == 0) return null;

		var beatmapset = beatmaps[0].Beatmapset;

		var folder = BeatmapIngestionService.FindBeatmapsetFolder(storage, setId);
		if (folder is not null)
			return await BuildFromFolderAsync(folder, beatmapset, noVideo, cancellationToken);

		var oszPath = BeatmapIngestionService.FindBeatmapsetOsz(storage, setId);
		return oszPath is null ? null : await BuildFromOszAsync(oszPath, beatmapset, noVideo, cancellationToken);
	}

	/// <summary>Builds the download archive from a legacy extracted beatmapset folder.</summary>
	private static async Task<(byte[] Bytes, string FileName)?> BuildFromFolderAsync(string folder,
		Beatmapset beatmapset, bool noVideo, CancellationToken cancellationToken)
	{
		using var zipStream = new MemoryStream();
		var wroteAny = false;
		var suffixed = false;
		await using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
		{
			foreach (var filePath in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
			{
				var isVideo = VideoExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());
				if (noVideo && isVideo)
				{
					suffixed = true;
					continue;
				}

				var entry = archive.CreateEntry(Path.GetRelativePath(folder, filePath));
				await using var entryStream = await entry.OpenAsync(cancellationToken);
				await using var fileStream = File.OpenRead(filePath);
				await fileStream.CopyToAsync(entryStream, cancellationToken);
				wroteAny = true;
			}
		}

		if (!wroteAny) return null;

		var name = $"{beatmapset.Id} {beatmapset.Artist} - {beatmapset.Title}{(suffixed ? " [no video]" : "")}.osz";
		return (zipStream.ToArray(), name);
	}

	/// <summary>
	///     Builds the download archive from a beatmapset's canonical ".osz". When
	///     <paramref name="noVideo" /> is <see langword="false" />, the archive is already exactly what
	///     the caller wants, so its bytes are returned as-is rather than being rebuilt entry by entry.
	/// </summary>
	private static async Task<(byte[] Bytes, string FileName)?> BuildFromOszAsync(string oszPath,
		Beatmapset beatmapset, bool noVideo, CancellationToken cancellationToken)
	{
		if (!noVideo)
		{
			var bytes = await File.ReadAllBytesAsync(oszPath, cancellationToken);
			return (bytes, $"{beatmapset.Id} {beatmapset.Artist} - {beatmapset.Title}.osz");
		}

		using var zipStream = new MemoryStream();
		var wroteAny = false;
		var suffixed = false;
		await using (var source = await ZipFile.OpenReadAsync(oszPath, cancellationToken))
		await using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
		{
			foreach (var sourceEntry in source.Entries)
			{
				if (sourceEntry.Name.Length == 0) continue; // directory entry

				var isVideo = VideoExtensions.Contains(Path.GetExtension(sourceEntry.Name).ToLowerInvariant());
				if (isVideo)
				{
					suffixed = true;
					continue;
				}

				var entry = archive.CreateEntry(sourceEntry.FullName);
				await using var entryStream = await entry.OpenAsync(cancellationToken);
				await using var sourceStream = await sourceEntry.OpenAsync(cancellationToken);
				await sourceStream.CopyToAsync(entryStream, cancellationToken);
				wroteAny = true;
			}
		}

		if (!wroteAny) return null;

		var name = $"{beatmapset.Id} {beatmapset.Artist} - {beatmapset.Title}{(suffixed ? " [no video]" : "")}.osz";
		return (zipStream.ToArray(), name);
	}

	/// <summary>Per-beatmapset single-flight lock guarding <see cref="BuildAudioPreviewAsync" />'s ffmpeg call.</summary>
	private static readonly ConcurrentDictionary<int, SemaphoreSlim> AudioPreviewLocks = new();

	/// <summary>
	///     Generates the standard audio preview clip for a beatmapset.
	/// </summary>
	/// <remarks>
	///     The preview is extracted from the beatmapset's configured preview time and is suitable
	///     for serving through any endpoint-exposing audio previews. Concurrent requests for the
	///     same beatmapset's preview are serialized so a cache miss triggers at most one ffmpeg
	///     process rather than one per concurrent request.
	/// </remarks>
	/// <returns>
	///     The generated preview audio, or <see langword="null" /> with <c>Failed</c> false if the
	///     beatmapset or preview audio is unavailable, or <see langword="null" /> with <c>Failed</c>
	///     true if extraction itself failed.
	/// </returns>
	public static async Task<(byte[]? Clip, bool Failed)> BuildAudioPreviewAsync(int setId,
		IBeatmapRepository beatmapRepository, IBeatmapsetRepository beatmapsetRepository,
		IOptions<StorageOptions> storage, BeatmapsetAssetCache assetCache, IResponseCache cache,
		IAudioExtractor extractor, ILogger<BeatmapsetAssetBuilderLog> logger, CancellationToken cancellationToken)
	{
		var beatmapset = await beatmapsetRepository.FetchByIdAsync(setId, cancellationToken);
		if (beatmapset is null || beatmapset.IsPrivate) return (null, false);

		var cacheKey = ResponseCacheKeys.Preview(setId);
		var cached = await cache.GetAsync("preview", cacheKey, cancellationToken);
		if (cached is not null) return (cached, false);

		var previewLock = AudioPreviewLocks.GetOrAdd(setId, static _ => new SemaphoreSlim(1, 1));
		await previewLock.WaitAsync(cancellationToken);
		try
		{
			// Re-check now that this call owns the lock: a concurrent request may have already
			// populated the cache while this one was waiting.
			cached = await cache.GetAsync("preview", cacheKey, cancellationToken);
			if (cached is not null) return (cached, false);

			var beatmaps = await beatmapRepository.FetchAllBySetIdAsync(setId, false, cancellationToken);
			var preview = beatmaps.MinBy(b => b.Id);
			if (preview?.AudioFile is null) return (null, false);

			var audioPath =
				await BeatmapIngestionService.AudioFilePathAsync(storage.Value, assetCache, preview, cancellationToken);
			if (audioPath is null || !File.Exists(audioPath)) return (null, false);

			// A -1/null PreviewTime means "no custom preview point set".
			// osu! Bancho would cut that at 40% into the track; Basil just starts from the top of the file.
			var startMs = preview.PreviewTime is > 0 ? preview.PreviewTime.Value : 0;
			byte[] clip;
			try
			{
				clip = await extractor.ExtractAsync(audioPath, startMs, TimeSpan.FromSeconds(10), cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				// Most commonly: no ffmpeg binary on PATH. Caught here rather than left to propagate,
				// so a missing/broken ffmpeg installation degrades one endpoint instead of crashing the request
				// pipeline with an unhandled exception.
				logger.LogError(ex, "Audio preview extraction failed: BeatmapsetId={BeatmapsetId}", setId);
				return (null, true);
			}

			await cache.PutAsync("preview", cacheKey, clip, cancellationToken);
			return (clip, false);
		}
		finally
		{
			previewLock.Release();
			// Every beatmapset is a distinct key with no natural end-of-life event to remove it on,
			// so leaving the entry behind would grow this dictionary without bound for the server's
			// whole lifetime. Compare-and-remove (not a plain key removal) so a concurrent request
			// that already looked up this exact SemaphoreSlim instance is never affected by it being
			// dropped here.
			AudioPreviewLocks.TryRemove(new KeyValuePair<int, SemaphoreSlim>(setId, previewLock));
		}
	}
}
