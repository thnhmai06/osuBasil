using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Media;
using Basil.Application.Abstractions.Storage;
using Basil.Application.Configurations;
using Basil.Application.Services.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Infrastructure.Beatmaps;
using Basil.Web.Auth;
using Basil.Web.OpenApi;
using Basil.Web.Routing.Bancho;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>
///     Dedicated <c>ILogger&lt;T&gt;</c> category marker, because <see cref="BeatmapsetRoutes" /> is static and
///     can't be a type argument.
/// </summary>
internal sealed class BeatmapsetRoutesLog;

/// <summary>
///     Registers the REST endpoints for listing, querying, uploading, and downloading beatmapsets.
/// </summary>
/// <remarks>
///     Reads are public, though a private beatmapset is only visible to callers with a valid
///     admin key. Creating, replacing, updating, and deleting beatmapsets require administrator
///     authorization. Replacing and deleting are asynchronous: they return `202 Accepted` and the
///     change becomes observable shortly after.
/// </remarks>
internal static class BeatmapsetRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/beatmapsets` read, write, and file-download routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapBeatmapsetRoutes(this RouteGroupBuilder group)
	{
		group.MapGet("/beatmapsets", HandleList)
			.WithGroupName("basilapi")
			.WithName("listBeatmapsets")
			.WithSummary("List beatmapsets.")
			.WithDescription("""
			                 Returns a page of beatmapsets, each with its difficulty count.

			                 Query params: `page` (default 1) and `pageSize` (default 50). A private beatmapset is excluded entirely unless the caller carries a valid admin key.
			                 """)
			.WithTags("Beatmapsets")
			.Produces<PagedResult<BeatmapsetSummary>>()
			.WithExample(StatusCodes.Status200OK, new PagedResult<BeatmapsetSummary>(1, 50, 1, [SampleSummary()]));

		group.MapPost("/beatmapsets", HandleCreate)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("createBeatmapset")
			.WithSummary("Create a beatmapset.")
			.WithDescription("""
			                 Multipart upload, field name `file`, must be a `.osz` archive (a lone `.osu` file has no set context).

			                 Adds the archive's beatmaps to the server and returns `{ ingested }`, the number of beatmaps added or updated.
			                 """ + AdminKeyNote)
			.WithTags("Beatmapsets")
			.WithMultipartFileUpload()
			.Produces<IngestResult>(StatusCodes.Status201Created)
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status201Created, new IngestResult(5))
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("Only .osz uploads are accepted (a single .osu file has no set context)."));

		group.MapGet("/beatmapsets/{mapsetId:int}", HandleGet)
			.WithGroupName("basilapi")
			.WithName("getBeatmapset")
			.WithSummary("Get beatmapset details.")
			.WithDescription("""
			                 Returns the beatmapset's metadata plus `beatmaps`, the full list of difficulties under it.

			                 For a single difficulty's full detail, use `GET /beatmapsets/{mapsetId}/{beatmapId}`.

			                 Returns `404 Not Found` if the beatmapset doesn't exist, or it's private and the caller isn't admin.
			                 """)
			.WithTags("Beatmapsets")
			.Produces<BeatmapsetDetail>()
			.WithExample(StatusCodes.Status200OK, SampleDetail())
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPut("/beatmapsets/{mapsetId:int}", HandleReplace)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceBeatmapset")
			.WithSummary("Replace a beatmapset.")
			.WithDescription("""
			                 Multipart upload, field name `file`, must be a `.osz` archive. Replaces the beatmapset's files with the archive's contents.

			                 Asynchronous: returns `202 Accepted` immediately, and the change becomes visible shortly after.

			                 Returns `409 Conflict` if the beatmapset is frozen (see `PATCH /beatmapsets/{mapsetId}`), or `404 Not Found` if the beatmapset doesn't exist.
			                 """ + AdminKeyNote)
			.WithTags("Beatmapsets")
			.WithMultipartFileUpload()
			.Produces<MapsetOperationAccepted>(StatusCodes.Status202Accepted)
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status202Accepted, new MapsetOperationAccepted(321, "replace"))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Only .osz uploads are accepted."))
			.WithExample(StatusCodes.Status409Conflict,
				new ErrorResponse("This beatmapset is frozen and cannot be modified."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/beatmapsets/{mapsetId:int}", HandleDelete)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("deleteBeatmapset")
			.WithSummary("Delete a beatmapset.")
			.WithDescription("""
			                 Asynchronous: returns `202 Accepted` immediately, and the beatmapset disappears shortly after.

			                 Returns `409 Conflict` if the beatmapset is frozen (see `PATCH /beatmapsets/{mapsetId}`) or its files are currently in use, or `404 Not Found` if the beatmapset doesn't exist.
			                 """ + AdminKeyNote)
			.WithTags("Beatmapsets")
			.Produces<MapsetOperationAccepted>(StatusCodes.Status202Accepted)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status202Accepted, new MapsetOperationAccepted(321, "delete"))
			.WithExample(StatusCodes.Status409Conflict,
				new ErrorResponse("This beatmapset is frozen and cannot be deleted."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPatch("/beatmapsets/{mapsetId:int}", HandlePatch)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("updateBeatmapset")
			.WithSummary("Update a beatmapset.")
			.WithDescription("""
			                 Body: `{ frozen?, private? }`. Each field is applied only if present, and the updated beatmapset info is returned.

			                 `frozen` is a write-lock: while set, `PUT` and `DELETE /beatmapsets/{mapsetId}` are rejected with `409 Conflict` (this route itself is exempt, so unfreezing is always possible). `private` hides the beatmapset and every beatmap under it from non-admin listings and lookups.

			                 Returns `404 Not Found` if the beatmapset doesn't exist.
			                 """ + AdminKeyNote)
			.WithTags("Beatmapsets")
			.Produces<BeatmapsetDetail>()
			.WithExample(StatusCodes.Status200OK, SampleDetail() with { IsFrozen = true })
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}", HandleBeatmapInfo)
			.WithGroupName("basilapi")
			.WithName("getBeatmap")
			.WithSummary("Get beatmap details.")
			.WithDescription("""
			                 Returns the beatmap's difficulty and object-count metadata plus its parent beatmapset.

			                 For the background image, use `GET /beatmapsets/{mapsetId}/{beatmapId}/background`.

			                 Returns `404 Not Found` if the beatmap doesn't exist, doesn't belong to this beatmapset, or the parent beatmapset is private and the caller isn't admin.
			                 """)
			.WithTags("Beatmaps")
			.Produces<BeatmapDetail>()
			.WithExample(StatusCodes.Status200OK, SampleBeatmap().ToDetail(SampleSummary()))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/difficulty", HandleBeatmapDifficulty)
			.WithGroupName("basilapi")
			.WithName("getBeatmapDifficulty")
			.WithSummary("Get beatmap difficulty with mods.")
			.WithDescription("""
			                 Recomputes star rating, BPM, length, and CS/AR/OD/HP for the given `mods` bitflag query param (default 0, no mods) and `mode` query param (default: the beatmap's own mode; 0 = osu!, 1 = taiko, 2 = catch, 3 = mania).

			                 HR/EZ's CS/AR/OD/HP multiplier and DT/HT/NC's BPM/length rate scaling apply in every mode. DT/HT/NC's AR/OD time-window shift is osu!std-only; AR/OD pass through unadjusted in taiko/catch/mania.

			                 Invalid mod combinations (e.g. `EZ+HR`) are silently resolved the same way multiplayer room mods are, and the response's `mods` field echoes what was actually applied.

			                 Returns `400 Bad Request` if `mode` is out of range or the beatmap can't be analyzed under the requested ruleset, or `404 Not Found` if the beatmap doesn't exist, doesn't belong to this beatmapset, its file is missing, or the parent beatmapset is private and the caller isn't admin.
			                 """)
			.WithTags("Beatmaps")
			.Produces<BeatmapDifficultyResult>()
			.WithExample(StatusCodes.Status200OK,
				new BeatmapDifficultyResult(Mods.NoMod, SampleBeatmap().ToDetail(SampleSummary())))
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/download", HandleDownloadBeatmap)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmap")
			.WithSummary("Download a beatmap.")
			.WithDescription("""
			                 Serves the raw `.osu` difficulty file, as `application/x-osu-beatmap`.

			                 Returns `404 Not Found` if the beatmap doesn't exist, doesn't belong to this beatmapset, its file is missing, or the parent beatmapset is private and the caller isn't admin.
			                 """)
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/background", HandleDownloadBackground)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapBackground")
			.WithSummary("Download a beatmap background.")
			.WithDescription("""
			                 Serves the beatmap's background image. Content-Type is inferred from the file extension.

			                 Returns `404 Not Found` if the beatmap doesn't exist, doesn't belong to this beatmapset, has no recorded background image, its file is missing, or the parent beatmapset is private and the caller isn't admin.
			                 """)
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/background", HandleDownloadMapsetBackground)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapsetBackground")
			.WithSummary("Download beatmapset background.")
			.WithDescription("""
			                 Serves the preview background image for this set: the lowest-id beatmap's background. Content-Type is inferred from the file extension.

			                 Returns `404 Not Found` if the beatmapset doesn't exist, has no recorded preview background, its file is missing, or the beatmapset is private and the caller isn't admin.
			                 """)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/audio", HandleDownloadAudio)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapAudio")
			.WithSummary("Download beatmap audio.")
			.WithDescription("""
			                 Serves the beatmap's audio file. Content-Type is inferred from the file extension.

			                 Returns `404 Not Found` if the beatmap doesn't exist, doesn't belong to this beatmapset, has no recorded audio file, its file is missing, or the parent beatmapset is private and the caller isn't admin.
			                 """)
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/audio", HandleDownloadMapsetAudio)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapsetAudio")
			.WithSummary("Download beatmapset audio.")
			.WithDescription("""
			                 Serves the preview audio file for this set: the lowest-id beatmap's audio. Content-Type is inferred from the file extension.

			                 Returns `404 Not Found` if the beatmapset doesn't exist, has no recorded audio file, its file is missing, or the beatmapset is private and the caller isn't admin.
			                 """)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/video", HandleDownloadVideo)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapVideo")
			.WithSummary("Download beatmap video.")
			.WithDescription("""
			                 Serves the beatmap's video file. Content-Type is inferred from the file extension.

			                 Returns `404 Not Found` if the beatmap doesn't exist, doesn't belong to this beatmapset, has no video, the video file is missing, or the parent beatmapset is private and the caller isn't admin.
			                 """)
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/audiopreview", HandleAudioPreview)
			.WithGroupName("basilapi")
			.WithName("getBeatmapsetAudioPreview")
			.WithSummary("Get beatmapset audio preview.")
			.WithDescription("""
			                 Serves a 10-second mp3 clip (128kbps) cut from the beatmapset's preview beatmap's audio file, starting at its recorded preview time. This is the same clip the `b.` host serves at `/preview/{mapsetId}.mp3`.

			                 Returns `404 Not Found` if the beatmapset doesn't exist, is private, or has no audio file, or `503 Service Unavailable` if audio extraction is temporarily unavailable.
			                 """)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/beatmapsets/{mapsetId:int}/storyboard", HandleDownloadStoryboard)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapsetStoryboard")
			.WithSummary("Download beatmapset storyboard.")
			.WithDescription("""
			                 Serves the beatmapset's `.osb` storyboard file, as `application/x-osu-storyboard`. A beatmapset is expected to carry at most one; if more than one is present, the first in filename order is served.

			                 Returns `404 Not Found` if the beatmapset has no `.osb` file.
			                 """)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/download", HandleDownloadArchive)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapset")
			.WithSummary("Download a beatmapset.")
			.WithDescription("""
			                 Serves a `.osz` archive containing every file in the beatmapset: audio, images, video, and every `.osu`/`.osb`. Served as `application/x-osu-beatmap-archive`.

			                 Returns `404 Not Found` if the beatmapset has no files.
			                 """)
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static BeatmapsetSummary SampleSummary()
	{
		var created = DateTime.Parse("2026-06-01T10:00:00Z");
		return new BeatmapsetSummary(321, "Camellia", "Exit This Earth's Atmosphere", "RLC", created, created,
			false, false, BeatmapStatus.Approved, 1);
	}

	private static BeatmapsetDetail SampleDetail()
	{
		var s = SampleSummary();
		return new BeatmapsetDetail(s.Id, s.Artist, s.Title, s.Creator, s.LastUpdate, s.CreatedAt, s.IsFrozen,
			s.IsPrivate, s.Status, [SampleBeatmap().ToInSet()]);
	}

	private static Beatmap SampleBeatmap()
	{
		var created = DateTime.Parse("2026-06-01T10:00:00Z");
		var mapset = new Beatmapset(321, "Camellia", "Exit This Earth's Atmosphere", "RLC", created, created);
		var difficulty = new Difficulty(GameMode.Standard, 174, TimeSpan.FromSeconds(225), 4, 9, 8, 6, 6.42);
		var objectCounts = new OsuBeatmapObjectCounts
			{ Total = 832, MaxCombo = 1234, Circles = 620, Sliders = 210, Spinners = 2 };
		return new Beatmap("d41d8cd98f00b204e9800998ecf8427e", 654, mapset, "Extreme",
			"camellia - exit this earth's atmosphere (rlc) [extreme].osu",
			difficulty, objectCounts);
	}

	private static async Task<IResult> HandleList([FromQuery] int? page, [FromQuery] int? pageSize,
		HttpContext context, IBeatmapsetRepository beatmapsetRepository, IBeatmapRepository beatmaps,
		CancellationToken cancellationToken)
	{
		var (p, ps) = Pagination.Normalize(page, pageSize);
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);

		var overqueried = await beatmapsetRepository.FetchPageAsync((p - 1) * ps, ps + 1, !isAdmin, cancellationToken);
		var totalRecords = await beatmapsetRepository.FetchCountAsync(isAdmin, cancellationToken);
		var items = new List<BeatmapsetSummary>(overqueried.Count);
		foreach (var m in overqueried)
		{
			var beatmapCount = (await beatmaps.FetchAllBySetIdAsync(m.Id, isAdmin, cancellationToken)).Count;
			items.Add(m.ToSummary(beatmapCount));
		}

		return Results.Json(Pagination.Trim(items, p, ps, totalRecords));
	}

	private static async Task<IResult> HandleCreate(HttpContext context, IOptions<StorageOptions> storage,
		BeatmapIngestionService ingestion, ILogger<BeatmapsetRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

		var form = await context.Request.ReadFormAsync(cancellationToken);
		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

		var extension = Path.GetExtension(file.FileName);
		if (!string.Equals(extension, ".osz", StringComparison.OrdinalIgnoreCase))
			return Results.BadRequest(
				new ErrorResponse("Only .osz uploads are accepted (a single .osu file has no set context)."));

		Directory.CreateDirectory(storage.Value.MapsetsPath);
		var destinationName = $"{Guid.NewGuid():N}{extension}";
		var destination = Path.Combine(storage.Value.MapsetsPath, Path.GetFileName(destinationName));
		await using (var fileStream = File.Create(destination))
		{
			await file.CopyToAsync(fileStream, cancellationToken);
		}

		var ingested = await ingestion.ReconcileAllAsync(cancellationToken);
		logger.LogInformation("Beatmapset created via admin API: IngestedCount={IngestedCount}", ingested);
		// No single canonical Location: one .osz upload can ingest/update more than one beatmapset via the
		// full reconciliation pass, so this can't point at one specific created resource like a normal
		// 201 would.
		return Results.Json(new IngestResult(ingested), statusCode: StatusCodes.Status201Created);
	}

	private static async Task<IResult> HandleGet(int mapsetId, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository,
		IBeatmapRepository beatmapRepository, CancellationToken cancellationToken)
	{
		var mapset = await beatmapsetRepository.FetchByIdAsync(mapsetId, cancellationToken);
		if (mapset is null) return Results.NotFound();

		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		if (mapset.IsPrivate && !isAdmin) return Results.NotFound();

		var beatmaps = await beatmapRepository.FetchAllBySetIdAsync(mapsetId, isAdmin, cancellationToken);
		return Results.Json(mapset.ToDetail(beatmaps));
	}

	private static async Task<IResult> HandleReplace(int mapsetId, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository,
		IOptions<StorageOptions> storage, ILogger<BeatmapsetRoutesLog> logger, CancellationToken cancellationToken)
	{
		var mapset = await beatmapsetRepository.FetchByIdAsync(mapsetId, cancellationToken);
		if (mapset is null) return Results.NotFound();
		if (mapset.IsFrozen)
			return Results.Conflict(new ErrorResponse("This beatmapset is frozen and cannot be modified."));

		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));
		var form = await context.Request.ReadFormAsync(cancellationToken);
		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));
		if (!string.Equals(Path.GetExtension(file.FileName), ".osz", StringComparison.OrdinalIgnoreCase))
			return Results.BadRequest(new ErrorResponse("Only .osz uploads are accepted."));

		var targetFolder = BeatmapIngestionService.FindMapsetFolder(storage.Value, mapsetId);
		if (targetFolder is null) return Results.NotFound();

		var tempOszPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.osz");
		await using (var fileStream = File.Create(tempOszPath))
		{
			await file.CopyToAsync(fileStream, cancellationToken);
		}

		try
		{
			await BeatmapIngestionService.ExtractOszIntoFolderAsync(tempOszPath, targetFolder, cancellationToken);
		}
		finally
		{
			File.Delete(tempOszPath);
		}

		logger.LogInformation("Beatmapset replace accepted via admin API: MapsetId={MapsetId}", mapsetId);
		return Results.Accepted(value: new MapsetOperationAccepted(mapsetId, "replace"));
	}

	private static async Task<IResult> HandleDelete(int mapsetId, IBeatmapsetRepository beatmapsetRepository,
		IOptions<StorageOptions> storage, ILogger<BeatmapsetRoutesLog> logger, CancellationToken cancellationToken)
	{
		var mapset = await beatmapsetRepository.FetchByIdAsync(mapsetId, cancellationToken);
		if (mapset is null) return Results.NotFound();
		if (mapset.IsFrozen)
			return Results.Conflict(new ErrorResponse("This beatmapset is frozen and cannot be deleted."));

		var folder = BeatmapIngestionService.FindMapsetFolder(storage.Value, mapsetId);
		if (folder is null) return Results.NotFound();

		var deletedFolder = folder + BeatmapIngestionService.DeletedFolderInfix + Guid.NewGuid().ToString("N");
		try
		{
			Directory.Move(folder, deletedFolder);
		}
		catch (IOException)
		{
			return Results.Conflict(
				new ErrorResponse("The beatmapset's files are currently in use; try again shortly."));
		}

		logger.LogInformation("Beatmapset delete accepted via admin API: MapsetId={MapsetId}", mapsetId);
		return Results.Accepted(value: new MapsetOperationAccepted(mapsetId, "delete"));
	}

	private static async Task<IResult> HandlePatch(int mapsetId, BeatmapsetPatchBody body,
		IBeatmapsetRepository beatmapsetRepository, IBeatmapRepository beatmapRepository,
		ILogger<BeatmapsetRoutesLog> logger,
		CancellationToken cancellationToken)
	{
		if (await beatmapsetRepository.FetchByIdAsync(mapsetId, cancellationToken) is null) return Results.NotFound();

		if (body.Frozen is not null)
			await beatmapsetRepository.SetFrozenAsync(mapsetId, body.Frozen.Value, cancellationToken);
		if (body.Private is not null)
			await beatmapsetRepository.SetPrivateAsync(mapsetId, body.Private.Value, cancellationToken);
		logger.LogInformation("Beatmapset updated via admin API: MapsetId={MapsetId} Frozen={Frozen} Private={Private}",
			mapsetId, body.Frozen, body.Private);

		var updated = await beatmapsetRepository.FetchByIdAsync(mapsetId, cancellationToken);
		var beatmaps = await beatmapRepository.FetchAllBySetIdAsync(mapsetId, true,
			cancellationToken);
		return Results.Json(updated!.ToDetail(beatmaps));
	}

	private static async Task<IResult> HandleBeatmapInfo(int mapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != mapsetId) return Results.NotFound();

		var siblings = await beatmaps.FetchAllBySetIdAsync(mapsetId, isAdmin, cancellationToken);
		var beatmapset = bmap.Beatmapset.ToSummary(siblings.Count);
		return Results.Json(bmap.ToDetail(beatmapset));
	}

	private static async Task<IResult> HandleBeatmapDifficulty(int mapsetId, int beatmapId,
		[FromQuery] int? mode, [FromQuery] uint? mods, HttpContext context, IBeatmapRepository beatmaps,
		IOsuCalculator calculator, IOptions<StorageOptions> storage, CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != mapsetId) return Results.NotFound();

		if (mode is < 0 or > 3)
			return Results.BadRequest(new ErrorResponse(
				$"Unknown mode '{mode}'. Valid values: 0 (osu!), 1 (taiko), 2 (catch), 3 (mania)."));
		var resolvedMode = mode is { } m ? (GameMode)m : bmap.Difficulty.Mode;
		var resolvedMods = ((Mods)(mods ?? 0)).FilterInvalidCombos(resolvedMode);

		var osuPath = BeatmapIngestionService.OsuFilePath(storage.Value, bmap);
		if (!File.Exists(osuPath)) return Results.NotFound();

		BeatmapAnalysis analysis;
		try
		{
			analysis = calculator.Analyze(osuPath, resolvedMode, resolvedMods);
		}
		catch (InvalidOperationException)
		{
			return Results.BadRequest(new ErrorResponse(
				$"Beatmap can't be analyzed as mode {resolvedMode}: likely an unsupported ruleset conversion."));
		}

		var siblings = await beatmaps.FetchAllBySetIdAsync(mapsetId, isAdmin, cancellationToken);
		var beatmapset = bmap.Beatmapset.ToSummary(siblings.Count);
		var detail = new BeatmapDetail(bmap.Md5, bmap.Id, bmap.Version, analysis.Difficulty,
			analysis.ObjectCounts, bmap.IsLocallyIngested, beatmapset);

		return Results.Json(new BeatmapDifficultyResult(resolvedMods, detail));
	}

	private static async Task<IResult> HandleDownloadBeatmap(int mapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != mapsetId) return Results.NotFound();

		var osuPath = BeatmapIngestionService.OsuFilePath(storage.Value, bmap);
		return File.Exists(osuPath) ? Results.File(osuPath, ContentTypes.Resolve(osuPath)) : Results.NotFound();
	}

	private static async Task<IResult> HandleDownloadBackground(int mapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != mapsetId) return Results.NotFound();

		var backgroundPath = BeatmapIngestionService.BackgroundFilePath(storage.Value, bmap);
		if (backgroundPath is null || !File.Exists(backgroundPath)) return Results.NotFound();

		return Results.File(backgroundPath, BackgroundContentType(backgroundPath));
	}

	private static string BackgroundContentType(string path)
	{
		return ContentTypes.Resolve(path, "image/jpeg");
	}

	private static async Task<IResult> HandleDownloadMapsetBackground(int mapsetId, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage,
		CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var mapset = await beatmapsetRepository.FetchByIdAsync(mapsetId, cancellationToken);
		if (mapset is null || (mapset.IsPrivate && !isAdmin)) return Results.NotFound();

		var backgroundPath = BeatmapIngestionService.BackgroundFilePath(storage.Value, mapset);
		if (backgroundPath is null || !File.Exists(backgroundPath)) return Results.NotFound();

		return Results.File(backgroundPath, BackgroundContentType(backgroundPath));
	}

	private static async Task<IResult> HandleDownloadAudio(int mapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != mapsetId) return Results.NotFound();

		var audioPath = BeatmapIngestionService.AudioFilePath(storage.Value, bmap);
		if (audioPath is null || !File.Exists(audioPath)) return Results.NotFound();

		return Results.File(audioPath, AudioContentType(audioPath));
	}

	private static string AudioContentType(string path)
	{
		return ContentTypes.Resolve(path, "audio/mpeg");
	}

	private static async Task<IResult> HandleDownloadMapsetAudio(int mapsetId, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage,
		CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var mapset = await beatmapsetRepository.FetchByIdAsync(mapsetId, cancellationToken);
		if (mapset is null || (mapset.IsPrivate && !isAdmin)) return Results.NotFound();

		var audioPath = BeatmapIngestionService.AudioFilePath(storage.Value, mapset);
		if (audioPath is null || !File.Exists(audioPath)) return Results.NotFound();

		return Results.File(audioPath, AudioContentType(audioPath));
	}

	private static async Task<IResult> HandleDownloadVideo(int mapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, IOptions<StorageOptions> storage, CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != mapsetId) return Results.NotFound();

		var videoPath = BeatmapIngestionService.VideoFilePath(storage.Value, bmap);
		if (videoPath is null || !File.Exists(videoPath)) return Results.NotFound();

		return Results.File(videoPath, VideoContentType(videoPath));
	}

	private static string VideoContentType(string path)
	{
		return ContentTypes.Resolve(path);
	}

	private static async Task<IResult> HandleAudioPreview(int mapsetId, IBeatmapRepository beatmaps,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IResponseCache cache,
		IAudioExtractor extractor, ILogger<BanchoHostGroupsLog> logger, CancellationToken cancellationToken)
	{
		var (clip, failed) = await BanchoHostGroups.BuildAudioPreviewAsync(mapsetId, beatmaps, beatmapsetRepository,
			storage, cache, extractor, logger, cancellationToken);
		if (failed) return Results.Problem("Audio preview extraction is temporarily unavailable.", statusCode: 503);
		return clip is null ? Results.NotFound() : Results.File(clip, "audio/mpeg");
	}

	private static IResult HandleDownloadStoryboard(int mapsetId, IOptions<StorageOptions> storage)
	{
		var folder = BeatmapIngestionService.FindMapsetFolder(storage.Value, mapsetId);
		if (folder is null) return Results.NotFound();

		var osbPath = Directory.EnumerateFiles(folder, "*.osb").Order().FirstOrDefault();
		return osbPath is null ? Results.NotFound() : Results.File(osbPath, ContentTypes.Resolve(osbPath));
	}

	private static async Task<IResult> HandleDownloadArchive(int mapsetId, IBeatmapRepository beatmaps,
		IOptions<StorageOptions> storage, CancellationToken cancellationToken)
	{
		var osz = await BanchoHostGroups.BuildBeatmapsetArchiveAsync(beatmaps, storage.Value, mapsetId, false,
			cancellationToken);
		return osz is null
			? Results.NotFound()
			: Results.File(osz.Value.Bytes, ContentTypes.Resolve(osz.Value.FileName), osz.Value.FileName);
	}

	/// <summary>Request body for `PATCH /beatmapsets/{mapsetId}`: each field is applied only if present.</summary>
	public sealed record BeatmapsetPatchBody(bool? Frozen = null, bool? Private = null);

	private sealed record IngestResult(int Ingested);

	/// <summary>Response body for the asynchronous `PUT`/`DELETE /beatmapsets/{mapsetId}` 202 responses.</summary>
	public sealed record MapsetOperationAccepted(int MapsetId, string Operation);

	/// <summary>
	///     Response for `GET /beatmapsets/{mapsetId}/{beatmapId}/difficulty`. The `mods` field echoes the
	///     actually applied mod combination, which can differ from the raw `mods` query param the caller sent
	///     because conflicting pairs like `EZ+HR` are silently resolved.
	/// </summary>
	public sealed record BeatmapDifficultyResult(Mods Mods, BeatmapDetail Beatmap);
}