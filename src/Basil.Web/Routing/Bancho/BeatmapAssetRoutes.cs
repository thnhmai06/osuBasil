using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Media;
using Basil.Application.Abstractions.Storage;
using Basil.Application.Configurations;
using Basil.Domain.Beatmaps;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing.Bancho;

/// <summary>
///     Registers the `b.{domain}` host's routes: the beatmapset thumbnail mirror-fallback (the
///     thumbnails themselves are served by <c>BeatmapThumbnailProvider</c>, ahead of routing)
///     and audio previews, trimmed on demand.
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
		// beatmapset's locally stored background by BeatmapThumbnailProvider (ImageSharp.Web),
		// which runs ahead of routing and so handles a set with a local background on its own —
		// these routes only run as a fallback: private/missing set, or no local background. When
		// the server runs in online mirror mode (see MirrorOptions.IsOnlineMode) and a set has no
		// local background, the request redirects to b.ppy.sh instead of 404ing, matching that
		// host's real public URL scheme — but only for sets with a genuine ppy id; a locally
		// authored set has no ppy-hosted counterpart to redirect to.
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

	/// <summary>Serves the 80x60 cover thumbnail's mirror fallback (no local background).</summary>
	private static Task<IResult> HandleThumbnailSmall(int setId, IBeatmapsetRepository beatmapsetRepository,
		IOptions<MirrorOptions> mirror, HttpRequest request, CancellationToken cancellationToken)
	{
		return HandleThumbnailFallbackAsync(setId, beatmapsetRepository, mirror, request, cancellationToken);
	}

	/// <summary>Serves the 160x120 list-icon thumbnail's mirror fallback (no local background).</summary>
	private static Task<IResult> HandleThumbnailLarge(int setId, IBeatmapsetRepository beatmapsetRepository,
		IOptions<MirrorOptions> mirror, HttpRequest request, CancellationToken cancellationToken)
	{
		return HandleThumbnailFallbackAsync(setId, beatmapsetRepository, mirror, request, cancellationToken);
	}

	/// <summary>
	///     Handles a thumbnail request that <c>BeatmapThumbnailProvider</c> didn't resolve: the
	///     beatmapset doesn't exist, is private, or has no local background. A beatmapset with a local
	///     background is always served by that provider instead — this handler never resizes anything.
	/// </summary>
	private static async Task<IResult> HandleThumbnailFallbackAsync(int setId,
		IBeatmapsetRepository beatmapsetRepository, IOptions<MirrorOptions> mirror, HttpRequest request,
		CancellationToken cancellationToken)
	{
		var mapset = await beatmapsetRepository.FetchByIdAsync(setId, cancellationToken);
		if (mapset is null || mapset.IsPrivate) return Results.NotFound();

		return MirrorFallback(mirror.Value, mapset, request);
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
	///     redirect with (a locally authored set has no mirror counterpart).
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