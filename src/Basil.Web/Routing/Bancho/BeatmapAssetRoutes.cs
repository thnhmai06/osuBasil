using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Media;
using Basil.Application.Abstractions.Storage;
using Basil.Application.Configurations;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing.Bancho;

/// <summary>
///     Registers the `b.{domain}` host's routes: beatmapset thumbnails and audio previews, resized
///     or trimmed on demand.
/// </summary>
internal static class BeatmapAssetRoutes
{
	/// <summary>
	///     Registers the `b.{domain}` host's routes.
	/// </summary>
	/// <param name="group">The `b.{domain}` route group.</param>
	public static void MapBeatmapAssetGroup(this RouteGroupBuilder group)
	{
		// The cover (80x60) and list-icon ("l", 160x120) thumbnails are resized directly from the
		// beatmapset's locally stored background on the first request and cached under
		// Data/Cache/thumb/ (see IResponseCache), instead of redirecting to the api. host's
		// unresized background route. When the server runs in online mirror mode (see
		// MirrorOptions.IsOnlineMode) and a set has no local background, the request redirects to
		// b.ppy.sh instead of 404ing, matching that host's real public URL scheme — but only for
		// sets with a genuine ppy id; a locally authored set has no ppy-hosted counterpart to redirect to.
		group.MapGet("/thumb/{setId:int}l.jpg", HandleThumbnailLarge)
			.WithGroupName("beatmapassets")
			.WithSummary("Retrieve a beatmapset list-icon thumbnail (160x120)")
			.WithDescription("Returns the beatmapset's cover image resized to 160x120 (cropped to fill).\n\n" +
			                 "Redirects to `b.ppy.sh` when the server runs in online mirror mode and this beatmapset " +
			                 "has a genuine ppy id but no local background.\n\n" +
			                 "404 — Beatmapset not found, private, or has no background image (locally-authored " +
			                 "sets have no mirror to fall back to).")
			.WithTags("Beatmap Assets");

		group.MapGet("/thumb/{setId:int}.jpg", HandleThumbnailSmall)
			.WithGroupName("beatmapassets")
			.WithSummary("Retrieve a beatmapset cover thumbnail (80x60)")
			.WithDescription("Returns the beatmapset's cover image resized to 80x60 (cropped to fill).\n\n" +
			                 "Redirects to `b.ppy.sh` when the server runs in online mirror mode and this beatmapset " +
			                 "has a genuine ppy id but no local background.\n\n" +
			                 "404 — Beatmapset not found, private, or has no background image (locally-authored " +
			                 "sets have no mirror to fall back to).")
			.WithTags("Beatmap Assets");

		group.MapGet("/preview/{setId:int}.mp3", HandleAudioPreview)
			.WithGroupName("beatmapassets")
			.WithSummary("Retrieve a beatmapset audio preview (10s mp3 clip)")
			.WithDescription("Returns a 10-second mp3 clip (128kbps) cut from the beatmapset's preview audio, " +
			                 "starting at its recorded preview time.\n\n" +
			                 "Redirects to `b.ppy.sh` when the server runs in online mirror mode and this beatmapset " +
			                 "has a genuine ppy id but no local preview audio.\n\n" +
			                 "404 — Beatmapset not found, private, or has no audio file (locally-authored sets have " +
			                 "no mirror to fall back to). 503 — audio extraction is temporarily unavailable.")
			.WithTags("Beatmap Assets");
	}

	/// <summary>Serves the 80x60 cover thumbnail for a beatmapset, resized on demand and cached.</summary>
	private static Task<IResult> HandleThumbnailSmall(int setId, IBeatmapsetRepository beatmapsetRepository,
		IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror, IResponseCache cache,
		IImageResizer resizer, HttpRequest request, CancellationToken cancellationToken)
	{
		return HandleThumbnailAsync(setId, false, 80, 60, beatmapsetRepository, storage, mirror, cache, resizer,
			request, cancellationToken);
	}

	/// <summary>Serves the 160x120 list-icon thumbnail for a beatmapset, resized on demand and cached.</summary>
	private static Task<IResult> HandleThumbnailLarge(int setId, IBeatmapsetRepository beatmapsetRepository,
		IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror, IResponseCache cache,
		IImageResizer resizer, HttpRequest request, CancellationToken cancellationToken)
	{
		return HandleThumbnailAsync(setId, true, 160, 120, beatmapsetRepository, storage, mirror, cache, resizer,
			request, cancellationToken);
	}

	/// <summary>
	///     Resizes the beatmapset's background image to a fixed size and serves it directly, rather than
	///     redirecting to the `api.` host's own (unresized) background route, because a real osu! client
	///     expects a thumb-sized image at this exact URL. Cached under `Data/Cache/thumb/` (see
	///     <see cref="IResponseCache" />) so the resize only ever runs once per beatmapset per size.
	/// </summary>
	private static async Task<IResult> HandleThumbnailAsync(int setId, bool large, int width, int height,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		IResponseCache cache, IImageResizer resizer, HttpRequest request, CancellationToken cancellationToken)
	{
		var mapset = await beatmapsetRepository.FetchByIdAsync(setId, cancellationToken);
		if (mapset is null || mapset.IsPrivate) return Results.NotFound();

		var cacheKey = ResponseCacheKeys.Thumb(setId, large);
		var contentType = ContentTypes.Resolve(cacheKey);
		var cached = await cache.GetAsync("thumb", cacheKey, cancellationToken);
		if (cached is not null) return Results.File(cached, contentType);

		var backgroundPath = BeatmapIngestionService.BackgroundFilePath(storage.Value, mapset);
		if (backgroundPath is null || !File.Exists(backgroundPath))
			return MirrorFallback(mirror.Value, mapset, request);

		var sourceBytes = await File.ReadAllBytesAsync(backgroundPath, cancellationToken);
		var resized = await resizer.ResizeAsync(sourceBytes, width, height, cancellationToken);
		await cache.PutAsync("thumb", cacheKey, resized, cancellationToken);

		return Results.File(resized, contentType);
	}

	/// <summary>
	///     Serves the beatmapset's audio preview clip as `audio/mpeg`, 404 when no clip can be
	///     produced, or 503 when audio extraction itself fails.
	/// </summary>
	private static async Task<IResult> HandleAudioPreview(int setId, IBeatmapRepository beatmaps,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IOptions<MirrorOptions> mirror,
		IResponseCache cache, IAudioExtractor extractor, HttpRequest request,
		ILogger<BanchoHostGroupsLog> logger, CancellationToken cancellationToken)
	{
		var mapset = await beatmapsetRepository.FetchByIdAsync(setId, cancellationToken);
		if (mapset is null || mapset.IsPrivate) return Results.NotFound();

		var (clip, failed) = await BanchoHostGroups.BuildAudioPreviewAsync(setId, beatmaps, beatmapsetRepository,
			storage, cache, extractor, logger, cancellationToken);
		if (failed) return Results.Problem("Audio preview extraction is temporarily unavailable.", statusCode: 503);
		if (clip is not null) return Results.File(clip, ContentTypes.Resolve(ResponseCacheKeys.Preview(setId)));

		return MirrorFallback(mirror.Value, mapset, request);
	}

	/// <summary>
	///     Redirects to `b.ppy.sh` when online mirror mode is active and the set has a genuine ppy id;
	///     404 when offline (unchanged behavior); 503 when online but the set has no ppy id to
	///     redirect with (a locally-authored set has no mirror counterpart).
	/// </summary>
	private static IResult MirrorFallback(MirrorOptions mirror, Beatmapset mapset, HttpRequest request)
	{
		if (!mirror.IsOnlineMode) return Results.NotFound();

		if (!mapset.IsLocallyIngested)
			return Results.Redirect($"https://b.ppy.sh{request.Path}{request.QueryString}", true);

		return Results.Problem("This endpoint is not available while the server runs in online mirror mode.",
			statusCode: StatusCodes.Status503ServiceUnavailable);
	}
}
