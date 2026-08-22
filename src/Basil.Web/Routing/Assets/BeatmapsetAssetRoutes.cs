using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Media;
using Basil.Application.Abstractions.Storage;
using Basil.Application.Configurations;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps;
using Basil.Web.Auth;
using Basil.Web.Routing.Bancho;
using Microsoft.Extensions.Options;

// ReSharper disable ClassNeverInstantiated.Global

namespace Basil.Web.Routing.Assets;

/// <summary>
///     Registers the `assets.` host's beatmapset/beatmap file routes: background, cover variants,
///     audio, video, downloads, and storyboard.
/// </summary>
/// <remarks>
///     `background` and `covers/{variant}.jpg` are served by <c>BeatmapsetBackgroundProvider</c>
///     (ImageSharp.Web) ahead of routing for the common case; the handlers here only run as a
///     fallback (private beatmapset viewed by an admin, or no local background) and for the
///     non-image files (audio/video/downloads/storyboard), which ImageSharp.Web never touches.
/// </remarks>
internal static class BeatmapsetAssetRoutes
{
	private const string MirrorFallbackNote = " Returns `503 Service Unavailable` instead of `404` when the file " +
	                                          "is missing locally and the server runs in online mirror mode " +
	                                          "(this route has no mirror equivalent to redirect to).";

	/// <summary>
	///     Registers the beatmapset/beatmap file routes on the `assets.` host.
	/// </summary>
	/// <param name="group">The `assets.` host route group.</param>
	public static void MapBeatmapsetAssetRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/download", HandleDownloadBeatmap)
			.WithGroupName("assets")
			.WithName("downloadBeatmapAsset")
			.WithSummary("Download a beatmap.")
			.WithDescription("""
			                 Serves the raw `.osu` difficulty file, as `application/x-osu-beatmap`.

			                 Returns `404 Not Found` if the beatmap doesn't exist, doesn't belong to this beatmapset, its file is missing, or the parent beatmapset is private and the caller isn't admin.
			                 """ + MirrorFallbackNote)
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/background", HandleDownloadBackground)
			.WithGroupName("assets")
			.WithName("downloadBeatmapBackgroundAsset")
			.WithSummary("Download a beatmap background.")
			.WithDescription("""
			                 Serves the beatmap's background image. Content-Type is inferred from the file extension.

			                 Returns `404 Not Found` if the beatmap doesn't exist, doesn't belong to this beatmapset, has no recorded background image, its file is missing, or the parent beatmapset is private and the caller isn't admin.
			                 """ + MirrorFallbackNote)
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/beatmapsets/{mapsetId:int}/background", HandleDownloadMapsetBackground)
			.WithGroupName("assets")
			.WithName("downloadBeatmapsetBackgroundAsset")
			.WithSummary("Download beatmapset background.")
			.WithDescription("""
			                 Serves the preview background image for this set: the lowest-id beatmap's background. Content-Type is inferred from the file extension.

			                 Returns `404 Not Found` if the beatmapset doesn't exist, has no recorded preview background, its file is missing, or the beatmapset is private and the caller isn't admin.
			                 """ + MirrorFallbackNote)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/beatmapsets/{mapsetId:int}/covers/{variant}.jpg", HandleDownloadCover)
			.WithGroupName("assets")
			.WithName("downloadBeatmapsetCoverAsset")
			.WithSummary("Download a beatmapset cover variant.")
			.WithDescription("""
			                 Serves the preview background image for this set, cropped to a fixed size for the given `{variant}`: `cover`, `card`, `list`, or `slimcover`.

			                 Returns `404 Not Found` if the beatmapset doesn't exist, has no recorded preview background, its file is missing, the beatmapset is private and the caller isn't admin, or `{variant}` isn't one of the four listed above.
			                 """ + MirrorFallbackNote)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/audio", HandleDownloadAudio)
			.WithGroupName("assets")
			.WithName("downloadBeatmapAudioAsset")
			.WithSummary("Download beatmap audio.")
			.WithDescription("""
			                 Serves the beatmap's audio file. Content-Type is inferred from the file extension.

			                 Returns `404 Not Found` if the beatmap doesn't exist, doesn't belong to this beatmapset, has no recorded audio file, its file is missing, or the parent beatmapset is private and the caller isn't admin.
			                 """ + MirrorFallbackNote)
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/beatmapsets/{mapsetId:int}/audio", HandleDownloadMapsetAudio)
			.WithGroupName("assets")
			.WithName("downloadBeatmapsetAudioAsset")
			.WithSummary("Download beatmapset audio.")
			.WithDescription("""
			                 Serves the preview audio file for this set: the lowest-id beatmap's audio. Content-Type is inferred from the file extension.

			                 Returns `404 Not Found` if the beatmapset doesn't exist, has no recorded audio file, its file is missing, or the beatmapset is private and the caller isn't admin.
			                 """ + MirrorFallbackNote)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/video", HandleDownloadVideo)
			.WithGroupName("assets")
			.WithName("downloadBeatmapVideoAsset")
			.WithSummary("Download beatmap video.")
			.WithDescription("""
			                 Serves the beatmap's video file. Content-Type is inferred from the file extension.

			                 Returns `404 Not Found` if the beatmap doesn't exist, doesn't belong to this beatmapset, has no video, the video file is missing, or the parent beatmapset is private and the caller isn't admin.
			                 """ + MirrorFallbackNote)
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/beatmapsets/{mapsetId:int}/audiopreview", HandleAudioPreview)
			.WithGroupName("assets")
			.WithName("getBeatmapsetAudioPreviewAsset")
			.WithSummary("Get beatmapset audio preview.")
			.WithDescription("""
			                 Serves a 10-second mp3 clip (128kbps) cut from the beatmapset's preview beatmap's audio file, starting at its recorded preview time. This is the same clip the `b.` host serves at `/preview/{mapsetId}.mp3`.

			                 Returns `404 Not Found` if the beatmapset doesn't exist or is private. `503 Service Unavailable` if audio extraction fails, or if no audio file is available locally and the server runs in online mirror mode (this route has no mirror equivalent to redirect to).
			                 """)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/beatmapsets/{mapsetId:int}/storyboard", HandleDownloadStoryboard)
			.WithGroupName("assets")
			.WithName("downloadBeatmapsetStoryboardAsset")
			.WithSummary("Download beatmapset storyboard.")
			.WithDescription("""
			                 Serves the beatmapset's `.osb` storyboard file, as `application/x-osu-storyboard`. A beatmapset is expected to carry at most one; if more than one is present, the first in filename order is served.

			                 Returns `404 Not Found` if the beatmapset has no `.osb` file.
			                 """ + MirrorFallbackNote)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/beatmapsets/{mapsetId:int}/download", HandleDownloadArchive)
			.WithGroupName("assets")
			.WithName("downloadBeatmapsetAsset")
			.WithSummary("Download a beatmapset.")
			.WithDescription("""
			                 Serves a `.osz` archive containing every file in the beatmapset: audio, images, video, and every `.osu`/`.osb`. Served as `application/x-osu-beatmap-archive`.

			                 Returns `404 Not Found` if the beatmapset has no files locally and either the server is offline or the beatmapset has no genuine ppy id to redirect a mirror lookup with. Otherwise, when the server runs in online mirror mode, redirects to the configured mirror.
			                 """)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static async Task<IResult> HandleDownloadBeatmap(int mapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != mapsetId) return Results.NotFound();

		var osuPath = BeatmapIngestionService.OsuFilePath(storage.Value, bmap);
		return File.Exists(osuPath)
			? Results.File(osuPath, ContentTypes.Resolve(osuPath), enableRangeProcessing: true)
			: LocalOnlyFallback(mirror.Value);
	}

	private static async Task<IResult> HandleDownloadBackground(int mapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != mapsetId) return Results.NotFound();

		var backgroundPath = BeatmapIngestionService.BackgroundFilePath(storage.Value, bmap);
		if (backgroundPath is null || !File.Exists(backgroundPath)) return LocalOnlyFallback(mirror.Value);

		return Results.File(backgroundPath, BackgroundContentType(backgroundPath), enableRangeProcessing: true);
	}

	private static string BackgroundContentType(string path)
	{
		return ContentTypes.Resolve(path, "image/jpeg");
	}

	private static async Task<IResult> HandleDownloadMapsetBackground(int mapsetId, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var (mapset, notFound) = await ResolveMapsetAsync(mapsetId, context, beatmapsetRepository, cancellationToken);
		if (notFound is not null) return notFound;

		var backgroundPath = BeatmapIngestionService.BackgroundFilePath(storage.Value, mapset!);
		if (backgroundPath is null || !File.Exists(backgroundPath)) return LocalOnlyFallback(mirror.Value);

		return Results.File(backgroundPath, BackgroundContentType(backgroundPath), enableRangeProcessing: true);
	}

	private static async Task<IResult> HandleDownloadCover(int mapsetId, string variant, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		if (variant is not ("cover" or "card" or "list" or "slimcover")) return Results.NotFound();

		var (mapset, notFound) = await ResolveMapsetAsync(mapsetId, context, beatmapsetRepository, cancellationToken);
		if (notFound is not null) return notFound;

		var backgroundPath = BeatmapIngestionService.BackgroundFilePath(storage.Value, mapset!);
		if (backgroundPath is null || !File.Exists(backgroundPath)) return LocalOnlyFallback(mirror.Value);

		// This handler only serves the miss-path fallback (see class remarks) — an existing background
		// is normally served, already cropped, by BeatmapsetBackgroundProvider before routing.
		return Results.File(backgroundPath, BackgroundContentType(backgroundPath), enableRangeProcessing: true);
	}

	private static async Task<(Beatmapset? Mapset, IResult? NotFound)> ResolveMapsetAsync(int mapsetId,
		HttpContext context, IBeatmapsetRepository beatmapsetRepository, CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var mapset = await beatmapsetRepository.FetchByIdAsync(mapsetId, cancellationToken);
		return mapset is null || (mapset.IsPrivate && !isAdmin) ? (null, Results.NotFound()) : (mapset, null);
	}

	private static async Task<IResult> HandleDownloadAudio(int mapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != mapsetId) return Results.NotFound();

		var audioPath = BeatmapIngestionService.AudioFilePath(storage.Value, bmap);
		if (audioPath is null || !File.Exists(audioPath)) return LocalOnlyFallback(mirror.Value);

		return Results.File(audioPath, AudioContentType(audioPath), enableRangeProcessing: true);
	}

	private static string AudioContentType(string path)
	{
		return ContentTypes.Resolve(path, "audio/mpeg");
	}

	private static async Task<IResult> HandleDownloadMapsetAudio(int mapsetId, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var (mapset, notFound) = await ResolveMapsetAsync(mapsetId, context, beatmapsetRepository, cancellationToken);
		if (notFound is not null) return notFound;

		var audioPath = BeatmapIngestionService.AudioFilePath(storage.Value, mapset!);
		if (audioPath is null || !File.Exists(audioPath)) return LocalOnlyFallback(mirror.Value);

		return Results.File(audioPath, AudioContentType(audioPath), enableRangeProcessing: true);
	}

	private static async Task<IResult> HandleDownloadVideo(int mapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != mapsetId) return Results.NotFound();

		var videoPath = BeatmapIngestionService.VideoFilePath(storage.Value, bmap);
		if (videoPath is null || !File.Exists(videoPath)) return LocalOnlyFallback(mirror.Value);

		return Results.File(videoPath, ContentTypes.Resolve(videoPath), enableRangeProcessing: true);
	}

	private static async Task<IResult> HandleAudioPreview(int mapsetId, IBeatmapRepository beatmaps,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		IResponseCache cache, IAudioExtractor extractor, ILogger<BanchoHostGroupsLog> logger,
		CancellationToken cancellationToken)
	{
		var (clip, failed) = await BanchoHostGroups.BuildAudioPreviewAsync(mapsetId, beatmaps, beatmapsetRepository,
			storage, cache, extractor, logger, cancellationToken);
		if (failed) return Results.Problem("Audio preview extraction is temporarily unavailable.", statusCode: 503);
		if (clip is not null) return Results.File(clip, "audio/mpeg");

		var mapset = await beatmapsetRepository.FetchByIdAsync(mapsetId, cancellationToken);
		return mapset is null ? Results.NotFound() : LocalOnlyFallback(mirror.Value);
	}

	private static async Task<IResult> HandleDownloadStoryboard(int mapsetId,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var folder = BeatmapIngestionService.FindMapsetFolder(storage.Value, mapsetId);
		var osbPath = folder is null
			? null
			: Directory.EnumerateFiles(folder, "*.osb").Order().FirstOrDefault();
		if (osbPath is not null)
			return Results.File(osbPath, ContentTypes.Resolve(osbPath), enableRangeProcessing: true);

		var mapset = await beatmapsetRepository.FetchByIdAsync(mapsetId, cancellationToken);
		return mapset is null ? Results.NotFound() : LocalOnlyFallback(mirror.Value);
	}

	private static async Task<IResult> HandleDownloadArchive(int mapsetId, IBeatmapRepository beatmaps,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var osz = await BanchoHostGroups.BuildBeatmapsetArchiveAsync(beatmaps, storage.Value, mapsetId, false,
			cancellationToken);
		if (osz is not null)
			return Results.File(osz.Value.Bytes, ContentTypes.Resolve(osz.Value.FileName), osz.Value.FileName);

		if (!mirror.Value.IsOnlineMode) return Results.NotFound();

		var mapset = await beatmapsetRepository.FetchByIdAsync(mapsetId, cancellationToken);
		if (mapset is { IsLocallyIngested: true }) return LocalOnlyFallback(mirror.Value);
		if (mapset is null && mapsetId >= Beatmap.LocalIdFloor) return Results.NotFound();

		return Results.Redirect($"{mirror.Value.DownloadEndpoint}/{mapsetId}?n=1", true);
	}

	/// <summary>
	///     503 while the server runs in online mirror mode (this route has no mirror-hosted equivalent
	///     to redirect to); 404 while offline, matching the pre-online-mode behavior exactly.
	/// </summary>
	private static IResult LocalOnlyFallback(MirrorOptions mirror)
	{
		return mirror.IsOnlineMode
			? Results.Problem("This endpoint is not available while the server runs in online mirror mode.",
				statusCode: StatusCodes.Status503ServiceUnavailable)
			: Results.NotFound();
	}
}