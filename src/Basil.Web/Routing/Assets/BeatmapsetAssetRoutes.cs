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
		group.MapGet("/beatmapsets/{beatmapsetId:int}/{beatmapId:int}/download", HandleDownloadBeatmap)
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

		group.MapGet("/beatmapsets/{beatmapsetId:int}/{beatmapId:int}/background", HandleDownloadBackground)
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

		group.MapGet("/beatmapsets/{beatmapsetId:int}/background", HandleDownloadBeatmapsetBackground)
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

		group.MapGet("/beatmapsets/{beatmapsetId:int}/covers/{variant}.jpg", HandleDownloadCover)
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

		group.MapGet("/beatmapsets/{beatmapsetId:int}/{beatmapId:int}/audio", HandleDownloadAudio)
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

		group.MapGet("/beatmapsets/{beatmapsetId:int}/audio", HandleDownloadBeatmapsetAudio)
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

		group.MapGet("/beatmapsets/{beatmapsetId:int}/{beatmapId:int}/video", HandleDownloadVideo)
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

		group.MapGet("/beatmapsets/{beatmapsetId:int}/audiopreview", HandleAudioPreview)
			.WithGroupName("assets")
			.WithName("getBeatmapsetAudioPreviewAsset")
			.WithSummary("Get beatmapset audio preview.")
			.WithDescription("""
			                 Serves a 10-second mp3 clip (128kbps) cut from the beatmapset's preview beatmap's audio file, starting at its recorded preview time. This is the same clip the `b.` host serves at `/preview/{beatmapsetId}.mp3`.

			                 Returns `404 Not Found` if the beatmapset doesn't exist or is private. `503 Service Unavailable` if audio extraction fails, or if no audio file is available locally and the server runs in online mirror mode (this route has no mirror equivalent to redirect to).
			                 """)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/beatmapsets/{beatmapsetId:int}/storyboard", HandleDownloadStoryboard)
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

		group.MapGet("/beatmapsets/{beatmapsetId:int}/download", HandleDownloadArchive)
			.WithGroupName("assets")
			.WithName("downloadBeatmapsetAsset")
			.WithSummary("Download a beatmapset.")
			.WithDescription("""
			                 Serves a `.osz` archive containing every file in the beatmapset: audio, images, video, and every `.osu`/`.osb`. Served as `application/x-osu-beatmap-archive`.

			                 Pass `?noVideo=1` to omit video files from the archive. Only applies when the beatmapset's files are stored locally; a mirror redirect always omits video regardless of this parameter.

			                 Returns `404 Not Found` if the beatmapset has no files locally and either the server is offline or the beatmapset has no genuine ppy id to redirect a mirror lookup with. Otherwise, when the server runs in online mirror mode, redirects to the configured mirror.
			                 """)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static async Task<IResult> HandleDownloadBeatmap(int beatmapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: beatmapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != beatmapsetId) return Results.NotFound();

		var osuPath = BeatmapIngestionService.OsuFilePath(storage.Value, bmap);
		return File.Exists(osuPath)
			? Results.File(osuPath, ContentTypes.Resolve(osuPath), enableRangeProcessing: true)
			: LocalOnlyFallback(mirror.Value);
	}

	private static async Task<IResult> HandleDownloadBackground(int beatmapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: beatmapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != beatmapsetId) return Results.NotFound();

		var backgroundPath = BeatmapIngestionService.BackgroundFilePath(storage.Value, bmap);
		if (backgroundPath is null || !File.Exists(backgroundPath)) return LocalOnlyFallback(mirror.Value);

		return Results.File(backgroundPath, BackgroundContentType(backgroundPath), enableRangeProcessing: true);
	}

	private static string BackgroundContentType(string path)
	{
		return ContentTypes.Resolve(path, "image/jpeg");
	}

	private static async Task<IResult> HandleDownloadBeatmapsetBackground(int beatmapsetId, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var (beatmapset, notFound) = await ResolveBeatmapsetAsync(beatmapsetId, context, beatmapsetRepository, cancellationToken);
		if (notFound is not null) return notFound;

		var backgroundPath = BeatmapIngestionService.BackgroundFilePath(storage.Value, beatmapset!);
		if (backgroundPath is null || !File.Exists(backgroundPath)) return LocalOnlyFallback(mirror.Value);

		return Results.File(backgroundPath, BackgroundContentType(backgroundPath), enableRangeProcessing: true);
	}

	private static async Task<IResult> HandleDownloadCover(int beatmapsetId, string variant, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		if (variant is not ("cover" or "card" or "list" or "slimcover")) return Results.NotFound();

		var (beatmapset, notFound) = await ResolveBeatmapsetAsync(beatmapsetId, context, beatmapsetRepository, cancellationToken);
		if (notFound is not null) return notFound;

		var backgroundPath = BeatmapIngestionService.BackgroundFilePath(storage.Value, beatmapset!);
		if (backgroundPath is null || !File.Exists(backgroundPath)) return LocalOnlyFallback(mirror.Value);

		// This handler only serves the miss-path fallback (see class remarks) — an existing background
		// is normally served, already cropped, by BeatmapsetBackgroundProvider before routing.
		return Results.File(backgroundPath, BackgroundContentType(backgroundPath), enableRangeProcessing: true);
	}

	private static async Task<(Beatmapset? Beatmapset, IResult? NotFound)> ResolveBeatmapsetAsync(int beatmapsetId,
		HttpContext context, IBeatmapsetRepository beatmapsetRepository, CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var beatmapset = await beatmapsetRepository.FetchByIdAsync(beatmapsetId, cancellationToken);
		return beatmapset is null || (beatmapset.IsPrivate && !isAdmin) ? (null, Results.NotFound()) : (beatmapset, null);
	}

	private static async Task<IResult> HandleDownloadAudio(int beatmapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: beatmapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != beatmapsetId) return Results.NotFound();

		var audioPath = BeatmapIngestionService.AudioFilePath(storage.Value, bmap);
		if (audioPath is null || !File.Exists(audioPath)) return LocalOnlyFallback(mirror.Value);

		return Results.File(audioPath, AudioContentType(audioPath), enableRangeProcessing: true);
	}

	private static string AudioContentType(string path)
	{
		return ContentTypes.Resolve(path, "audio/mpeg");
	}

	private static async Task<IResult> HandleDownloadBeatmapsetAudio(int beatmapsetId, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var (beatmapset, notFound) = await ResolveBeatmapsetAsync(beatmapsetId, context, beatmapsetRepository, cancellationToken);
		if (notFound is not null) return notFound;

		var audioPath = BeatmapIngestionService.AudioFilePath(storage.Value, beatmapset!);
		if (audioPath is null || !File.Exists(audioPath)) return LocalOnlyFallback(mirror.Value);

		return Results.File(audioPath, AudioContentType(audioPath), enableRangeProcessing: true);
	}

	private static async Task<IResult> HandleDownloadVideo(int beatmapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: beatmapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != beatmapsetId) return Results.NotFound();

		var videoPath = BeatmapIngestionService.VideoFilePath(storage.Value, bmap);
		if (videoPath is null || !File.Exists(videoPath)) return LocalOnlyFallback(mirror.Value);

		return Results.File(videoPath, ContentTypes.Resolve(videoPath), enableRangeProcessing: true);
	}

	private static async Task<IResult> HandleAudioPreview(int beatmapsetId, IBeatmapRepository beatmaps,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		IResponseCache cache, IAudioExtractor extractor, ILogger<BanchoHostGroupsLog> logger,
		CancellationToken cancellationToken)
	{
		var (clip, failed) = await BanchoHostGroups.BuildAudioPreviewAsync(beatmapsetId, beatmaps, beatmapsetRepository,
			storage, cache, extractor, logger, cancellationToken);
		if (failed) return Results.Problem("Audio preview extraction is temporarily unavailable.", statusCode: 503);
		if (clip is not null) return Results.File(clip, "audio/mpeg");

		var beatmapset = await beatmapsetRepository.FetchByIdAsync(beatmapsetId, cancellationToken);
		return beatmapset is null ? Results.NotFound() : LocalOnlyFallback(mirror.Value);
	}

	private static async Task<IResult> HandleDownloadStoryboard(int beatmapsetId,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken)
	{
		var folder = BeatmapIngestionService.FindBeatmapsetFolder(storage.Value, beatmapsetId);
		var osbPath = folder is null
			? null
			: Directory.EnumerateFiles(folder, "*.osb").Order().FirstOrDefault();
		if (osbPath is not null)
			return Results.File(osbPath, ContentTypes.Resolve(osbPath), enableRangeProcessing: true);

		var beatmapset = await beatmapsetRepository.FetchByIdAsync(beatmapsetId, cancellationToken);
		return beatmapset is null ? Results.NotFound() : LocalOnlyFallback(mirror.Value);
	}

	private static async Task<IResult> HandleDownloadArchive(int beatmapsetId, IBeatmapRepository beatmaps,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		CancellationToken cancellationToken, int noVideo = 0)
	{
		var osz = await BanchoHostGroups.BuildBeatmapsetArchiveAsync(beatmaps, storage.Value, beatmapsetId,
			noVideo != 0, cancellationToken);
		if (osz is not null)
			return Results.File(osz.Value.Bytes, ContentTypes.Resolve(osz.Value.FileName), osz.Value.FileName);

		if (!mirror.Value.IsOnlineMode) return Results.NotFound();

		var beatmapset = await beatmapsetRepository.FetchByIdAsync(beatmapsetId, cancellationToken);
		if (beatmapset is { IsLocallyIngested: true }) return LocalOnlyFallback(mirror.Value);
		if (beatmapset is null && beatmapsetId >= Beatmap.LocalIdFloor) return Results.NotFound();

		return Results.Redirect($"{mirror.Value.DownloadEndpoint}/{beatmapsetId}?n=1", true);
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