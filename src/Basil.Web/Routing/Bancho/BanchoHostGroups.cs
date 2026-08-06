using System.Collections.Frozen;
using System.IO.Compression;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Media;
using Basil.Application.Abstractions.Storage;
using Basil.Application.Configurations;
using Basil.Infrastructure.Beatmaps;
using Basil.Web.Routing.Api;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing.Bancho;

/// <summary>
///     Dedicated <c>ILogger&lt;T&gt;</c> category marker, because <see cref="BanchoHostGroups" /> is static and
///     can't be a type argument.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
internal sealed class BanchoHostGroupsLog;

/// <summary>
///     Registers the host groups for both the configured domain and the fallback
///     <c>ppy.sh</c> domain.
/// </summary>
/// <remarks>
///     The following hosts are registered:
///     <list type="bullet">
///         <item><c>c.</c>, <c>ce.</c>, <c>c4.</c>, <c>c5.</c>, and <c>c6.</c> for the bancho protocol.</item>
///         <item><c>osu.</c> for the osu! web endpoints.</item>
///         <item><c>b.</c> for beatmap assets.</item>
///         <item><c>a.</c> for user avatars.</item>
///         <item><c>api.</c> for the Bancho REST API.</item>
///     </list>
/// </remarks>
public static class BanchoHostGroups
{
	private static readonly string[] BanchoSubdomains = ["c", "ce", "c4", "c5", "c6"];

	private static readonly FrozenSet<string> VideoExtensions =
		new[] { ".avi", ".flv", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".m4v", ".webm", ".wmv" }.ToFrozenSet(
			StringComparer.OrdinalIgnoreCase);

	/// <summary>
	///     Registers all Bancho host groups for the configured domain and the fallback
	///     <c>ppy.sh</c> domain.
	/// </summary>
	/// <param name="app"> The web application to register the host groups on. </param>
	/// <param name="configuredDomain"> The primary domain used to expose the host groups. </param>
	public static void MapAll(WebApplication app, string configuredDomain)
	{
		var domains = new[] { "ppy.sh", configuredDomain }.Distinct().ToArray();

		var banchoHosts = domains
			.SelectMany(domain => BanchoSubdomains.Select(subdomain => $"{subdomain}.{domain}"))
			.ToArray();
		var osuWebHosts = domains.Select(domain => $"osu.{domain}").ToArray();
		var beatmapAssetHosts = domains.Select(domain => $"b.{domain}").ToArray();
		var avatarHosts = domains.Select(domain => $"a.{domain}").ToArray();
		var apiHosts = domains.Select(domain => $"api.{domain}").ToArray();

		app.MapGroup("/").RequireHost(banchoHosts).MapBanchoGroup();
		app.MapGroup("/").RequireHost(osuWebHosts).MapOsuWebGroup();
		app.MapGroup("/").RequireHost(beatmapAssetHosts).MapBeatmapAssetGroup();
		app.MapGroup("/").RequireHost(avatarHosts).MapAvatarGroup();
		app.MapGroup("/").RequireHost(apiHosts).MapApiGroup();
	}

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
	internal static async Task<(byte[] Bytes, string FileName)?> BuildBeatmapsetArchiveAsync(
		IBeatmapRepository beatmapRepository,
		StorageOptions storage, int setId, bool noVideo, CancellationToken cancellationToken)
	{
		var beatmaps = await beatmapRepository.FetchAllBySetIdAsync(setId, cancellationToken: cancellationToken);
		if (beatmaps.Count == 0) return null;

		var mapset = beatmaps[0].Beatmapset;
		var folder = BeatmapIngestionService.FindMapsetFolder(storage, setId);
		if (folder is null) return null;

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

		var name = $"{mapset.Id} {mapset.Artist} - {mapset.Title}{(suffixed ? " [no video]" : "")}.osz";
		return (zipStream.ToArray(), name);
	}

	/// <summary>
	///     Generates the standard audio preview clip for a beatmapset.
	/// </summary>
	/// <remarks>
	///     The preview is extracted from the beatmapset's configured preview time and is suitable
	///     for serving through any endpoint-exposing audio previews.
	/// </remarks>
	/// <returns>
	///     The generated preview audio, or <see langword="null" /> with <c>Failed</c> false if the
	///     beatmapset or preview audio is unavailable, or <see langword="null" /> with <c>Failed</c>
	///     true if extraction itself failed.
	/// </returns>
	internal static async Task<(byte[]? Clip, bool Failed)> BuildAudioPreviewAsync(int setId,
		IBeatmapRepository beatmapRepository, IBeatmapsetRepository beatmapsetRepository,
		IOptions<StorageOptions> storage, IResponseCache cache, IAudioExtractor extractor,
		ILogger<BanchoHostGroupsLog> logger, CancellationToken cancellationToken)
	{
		var mapset = await beatmapsetRepository.FetchByIdAsync(setId, cancellationToken);
		if (mapset is null || mapset.IsPrivate) return (null, false);

		var cacheKey = ResponseCacheKeys.Preview(setId);
		var cached = await cache.GetAsync("preview", cacheKey, cancellationToken);
		if (cached is not null) return (cached, false);

		var beatmaps = await beatmapRepository.FetchAllBySetIdAsync(setId, false, cancellationToken);
		var preview = beatmaps.MinBy(b => b.Id);
		if (preview?.AudioFile is null) return (null, false);

		var audioPath = BeatmapIngestionService.AudioFilePath(storage.Value, preview);
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
			logger.LogError(ex, "Audio preview extraction failed: MapsetId={MapsetId}", setId);
			return (null, true);
		}

		await cache.PutAsync("preview", cacheKey, clip, cancellationToken);
		return (clip, false);
	}
}