using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configurations;
using Basil.Application.Services.Beatmaps;
using Basil.Domain.Beatmaps;
using Basil.Domain.Scores;
using Basil.Infrastructure.Beatmaps;
using Basil.Web.Auth;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>A dedicated logger category marker for the static <see cref="BeatmapsetRoutes" /> class.</summary>
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

		group.MapGet("/beatmapsets/search", HandleSearch)
			.WithGroupName("basilapi")
			.WithName("searchBeatmapsets")
			.WithSummary("Search beatmapsets.")
			.WithDescription("""
			                 Returns a page of beatmapsets matching `q`, an osu!-style search query: free-text keywords plus optional `key<operator>value` filters, operators `: = < <= > >=`.

			                 Supported filter keys: `stars`/`star`, `ar`, `hp`/`dr`, `cs`, `od`, `bpm`, `length` (seconds, or suffixed `s`/`m`/`h`/`ms`), `keys`/`key` (mania key count), `circles`, `sliders`, `creator`, `artist`, `title`, `difficulty`, `status` (`ranked`, `approved`, `qualified`, `loved`, `pending`/`graveyard`, by unambiguous prefix), `created`/`submitted`, `updated` (year, year-month, or year-month-day). A quoted value (`artist="foo bar"`) may contain spaces. An unrecognized filter key, or a value that fails to parse, falls back to a literal free-text keyword instead of erroring.

			                 Query params: `q` (the search query, default empty), `mode` (optional game mode filter: 0 osu!, 1 taiko, 2 catch, 3 mania), `page` (default 1), `pageSize` (default 50).

			                 A private beatmapset is always excluded from these results.
			                 """)
			.WithTags("Beatmapsets")
			.Produces<PagedResult<BeatmapsetSummary>>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new PagedResult<BeatmapsetSummary>(1, 50, 1, [SampleSummary()]))
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("Unknown mode '9'. Valid values: 0 (osu!), 1 (taiko), 2 (catch), 3 (mania)."));

		group.MapPost("/beatmapsets", HandleCreate)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("createBeatmapset")
			.WithSummary("Create a beatmapset.")
			.WithDescription("""
			                 Multipart upload, field name `file`. Only .osz uploads are accepted.

			                 Adds the archive's beatmaps to the server and returns the number of beatmaps added or updated by this upload specifically.
			                 """ + AdminKeyNote)
			.WithTags("Beatmapsets")
			.WithMultipartFileUpload()
			.Produces<IngestResult>(StatusCodes.Status201Created)
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status201Created, new IngestResult(5))
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("Only .osz uploads are accepted."));

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}", HandleGet)
			.WithGroupName("basilapi")
			.WithName("getBeatmapset")
			.WithSummary("Get beatmapset details.")
			.WithDescription("""
			                 Returns the beatmapset's metadata plus `beatmaps`, the full list of difficulties under it.

			                 For a single difficulty's full detail, use `GET /beatmapsets/{beatmapsetId}/{beatmapId}`.

			                 Returns `404 Not Found` if the beatmapset doesn't exist, or it's private and the caller isn't admin.
			                 """)
			.WithTags("Beatmapsets")
			.Produces<BeatmapsetDetail>()
			.WithExample(StatusCodes.Status200OK, SampleDetail())
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPut("/beatmapsets/{beatmapsetId:numericid}", HandleReplace)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceBeatmapset")
			.WithSummary("Replace a beatmapset.")
			.WithDescription("""
			                 Multipart upload, field name `file`, must be a `.osz` archive. Replaces the beatmapset's files with the archive's contents.

			                 Asynchronous: returns `202 Accepted` immediately, and the change becomes visible shortly after.

			                 Returns `409 Conflict` if the beatmapset is frozen (see `PATCH /beatmapsets/{beatmapsetId}`), or `404 Not Found` if the beatmapset doesn't exist.
			                 """ + AdminKeyNote)
			.WithTags("Beatmapsets")
			.WithMultipartFileUpload()
			.Produces<BeatmapsetOperationAccepted>(StatusCodes.Status202Accepted)
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status202Accepted, new BeatmapsetOperationAccepted(321, "replace"))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Only .osz uploads are accepted."))
			.WithExample(StatusCodes.Status409Conflict,
				new ErrorResponse("This beatmapset is frozen and cannot be modified."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/beatmapsets/{beatmapsetId:numericid}", HandleDelete)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("deleteBeatmapset")
			.WithSummary("Delete a beatmapset.")
			.WithDescription("""
			                 Asynchronous: returns `202 Accepted` immediately, and the beatmapset disappears shortly after.

			                 Returns `409 Conflict` if the beatmapset is frozen (see `PATCH /beatmapsets/{beatmapsetId}`) or its files are currently in use, or `404 Not Found` if the beatmapset doesn't exist.
			                 """ + AdminKeyNote)
			.WithTags("Beatmapsets")
			.Produces<BeatmapsetOperationAccepted>(StatusCodes.Status202Accepted)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status202Accepted, new BeatmapsetOperationAccepted(321, "delete"))
			.WithExample(StatusCodes.Status409Conflict,
				new ErrorResponse("This beatmapset is frozen and cannot be deleted."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPatch("/beatmapsets/{beatmapsetId:numericid}", HandlePatch)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("updateBeatmapset")
			.WithSummary("Update a beatmapset.")
			.WithDescription("""
			                 Body: `{ frozen?, private? }`. Each field is applied only if present, and the updated beatmapset info is returned.

			                 `frozen` is a write-lock: while set, `PUT` and `DELETE /beatmapsets/{beatmapsetId}` are rejected with `409 Conflict` (this route itself is exempt, so unfreezing is always possible). `private` hides the beatmapset and every beatmap under it from non-admin listings and lookups.

			                 Returns `404 Not Found` if the beatmapset doesn't exist.
			                 """ + AdminKeyNote)
			.WithTags("Beatmapsets")
			.Produces<BeatmapsetDetail>()
			.WithExample(StatusCodes.Status200OK, SampleDetail() with { IsFrozen = true })
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/{beatmapId:numericid}", HandleBeatmapInfo)
			.WithGroupName("basilapi")
			.WithName("getBeatmap")
			.WithSummary("Get beatmap details.")
			.WithDescription("""
			                 Returns the beatmap's difficulty and object-count metadata plus its parent beatmapset.

			                 For the background image, use `GET /beatmapsets/{beatmapsetId}/{beatmapId}/background`.

			                 Returns `404 Not Found` if the beatmap doesn't exist, doesn't belong to this beatmapset, or the parent beatmapset is private and the caller isn't admin.
			                 """)
			.WithTags("Beatmaps")
			.Produces<BeatmapDetail>()
			.WithExample(StatusCodes.Status200OK, SampleBeatmap().ToDetail(SampleSummary()))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/{beatmapId:numericid}/difficulty", HandleBeatmapDifficulty)
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

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/{beatmapId:numericid}/download", RedirectToAssets)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmap")
			.WithSummary("Download a beatmap.")
			.WithDescription("Redirects to `GET assets.<domain>/beatmapsets/{beatmapsetId}/{beatmapId}/download`, " +
			                 "which serves the raw `.osu` difficulty file.")
			.WithTags("Beatmaps")
			.Produces(StatusCodes.Status302Found);

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/{beatmapId:numericid}/background", RedirectToAssets)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapBackground")
			.WithSummary("Download a beatmap background.")
			.WithDescription("Redirects to `GET assets.<domain>/beatmapsets/{beatmapsetId}/{beatmapId}/background`, " +
			                 "which serves the image, resized on request (see `covers/{variant}.jpg` for fixed sizes).")
			.WithTags("Beatmaps")
			.Produces(StatusCodes.Status302Found);

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/background", RedirectToAssets)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapsetBackground")
			.WithSummary("Download beatmapset background.")
			.WithDescription("Redirects to `GET assets.<domain>/beatmapsets/{beatmapsetId}/background`, which " +
			                 "serves the preview background image for this set: the lowest-id beatmap's background.")
			.WithTags("Beatmapsets")
			.Produces(StatusCodes.Status302Found);

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/covers/{variant}.jpg", RedirectToAssets)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapsetCover")
			.WithSummary("Download a beatmapset cover variant.")
			.WithDescription(
				"Redirects to `GET assets.<domain>/beatmapsets/{beatmapsetId}/covers/{variant}.jpg`, which serves the set's preview background cropped to a fixed size for the given `{variant}`: `cover`, `card`, `list`, or `slimcover`.")
			.WithTags("Beatmapsets")
			.Produces(StatusCodes.Status302Found);

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/{beatmapId:numericid}/audio", RedirectToAssets)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapAudio")
			.WithSummary("Download beatmap audio.")
			.WithDescription("Redirects to `GET assets.<domain>/beatmapsets/{beatmapsetId}/{beatmapId}/audio`, " +
			                 "which serves the beatmap's audio file.")
			.WithTags("Beatmaps")
			.Produces(StatusCodes.Status302Found);

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/audio", RedirectToAssets)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapsetAudio")
			.WithSummary("Download beatmapset audio.")
			.WithDescription("Redirects to `GET assets.<domain>/beatmapsets/{beatmapsetId}/audio`, which serves " +
			                 "the preview audio file for this set: the lowest-id beatmap's audio.")
			.WithTags("Beatmapsets")
			.Produces(StatusCodes.Status302Found);

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/{beatmapId:numericid}/video", RedirectToAssets)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapVideo")
			.WithSummary("Download beatmap video.")
			.WithDescription("Redirects to `GET assets.<domain>/beatmapsets/{beatmapsetId}/{beatmapId}/video`, " +
			                 "which serves the beatmap's video file.")
			.WithTags("Beatmaps")
			.Produces(StatusCodes.Status302Found);

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/audiopreview", RedirectToAssets)
			.WithGroupName("basilapi")
			.WithName("getBeatmapsetAudioPreview")
			.WithSummary("Get beatmapset audio preview.")
			.WithDescription("Redirects to `GET assets.<domain>/beatmapsets/{beatmapsetId}/audiopreview`, which " +
			                 "serves a 10-second mp3 clip (128kbps) cut from the beatmapset's preview beatmap's " +
			                 "audio file, starting at its recorded preview time.")
			.WithTags("Beatmapsets")
			.Produces(StatusCodes.Status302Found);

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/storyboard", RedirectToAssets)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapsetStoryboard")
			.WithSummary("Download beatmapset storyboard.")
			.WithDescription("Redirects to `GET assets.<domain>/beatmapsets/{beatmapsetId}/storyboard`, which " +
			                 "serves the beatmapset's `.osb` storyboard file.")
			.WithTags("Beatmapsets")
			.Produces(StatusCodes.Status302Found);

		group.MapGet("/beatmapsets/{beatmapsetId:numericid}/download", RedirectToAssets)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapset")
			.WithSummary("Download a beatmapset.")
			.WithDescription("Redirects to `GET assets.<domain>/beatmapsets/{beatmapsetId}/download`, which " +
			                 "serves a `.osz` archive containing every file in the beatmapset.")
			.WithTags("Beatmapsets")
			.Produces(StatusCodes.Status302Found);
	}

	/// <summary>Redirects to the same path/query on the `assets.` host.</summary>
	private static IResult RedirectToAssets(HttpContext context, IOptions<ServerOptions> server)
	{
		return Results.Redirect(
			$"https://assets.{server.Value.Domain}{context.Request.Path}{context.Request.QueryString}");
	}

	private static BeatmapsetSummary SampleSummary()
	{
		var created = DateTimeOffset.Parse("2026-06-01T10:00:00Z");
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
		var beatmapset = new Beatmapset(321, "Camellia", "Exit This Earth's Atmosphere", "RLC", created, created);
		var difficulty = new Difficulty(GameMode.Standard, 174, TimeSpan.FromSeconds(225), 4, 9, 8, 6, 6.42);
		var objectCounts = new OsuBeatmapObjectCounts
			{ Total = 832, MaxCombo = 1234, Circles = 620, Sliders = 210, Spinners = 2 };
		return new Beatmap("d41d8cd98f00b204e9800998ecf8427e", 654, beatmapset, "Extreme",
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
		var counts = await beatmaps.FetchCountsBySetIdsAsync(
			[.. overqueried.Select(m => m.Id)], isAdmin, cancellationToken);
		var items = new List<BeatmapsetSummary>(overqueried.Count);
		foreach (var m in overqueried)
			items.Add(m.ToSummary(counts.GetValueOrDefault(m.Id)));

		return Results.Json(Pagination.Trim(items, p, ps, totalRecords));
	}

	private static async Task<IResult> HandleSearch([FromQuery] string? q, [FromQuery] int? mode,
		[FromQuery] int? page, [FromQuery] int? pageSize, IBeatmapRepository beatmaps,
		CancellationToken cancellationToken)
	{
		if (mode is < 0 or > 3)
			return Results.BadRequest(new ErrorResponse(
				$"Unknown mode '{mode}'. Valid values: 0 (osu!), 1 (taiko), 2 (catch), 3 (mania)."));
		var resolvedMode = mode is { } m ? (GameMode)m : (GameMode?)null;

		var (p, ps) = Pagination.Normalize(page, pageSize);
		var filters = BeatmapsetSearchQueryParser.Parse(q);

		var sets = await beatmaps.SearchAsync(filters, resolvedMode, (p - 1) * ps, ps, cancellationToken);
		var total = await beatmaps.SearchCountAsync(filters, resolvedMode, cancellationToken);
		var items = sets.Select(set => set[0].Beatmapset.ToSummary(set.Count)).ToList();

		return Results.Json(new PagedResult<BeatmapsetSummary>(p, ps, total, items));
	}

	private static async Task<IResult> HandleCreate(HttpContext context, IOptions<StorageOptions> storage,
		BeatmapIngestionService ingestion, ILogger<BeatmapsetRoutesLog> logger, CancellationToken cancellationToken)
	{
		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

		IFormCollection form;
		try
		{
			form = await context.Request.ReadFormAsync(cancellationToken);
		}
		catch (InvalidDataException)
		{
			return Results.BadRequest(new ErrorResponse("Malformed multipart upload."));
		}

		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

		var extension = Path.GetExtension(file.FileName);
		if (!string.Equals(extension, ".osz", StringComparison.OrdinalIgnoreCase))
			return Results.BadRequest(
				new ErrorResponse("Only .osz uploads are accepted."));

		Directory.CreateDirectory(storage.Value.BeatmapsetsPath);
		var destinationName = $"{Guid.NewGuid():N}{extension}";
		var destination = Path.Combine(storage.Value.BeatmapsetsPath, Path.GetFileName(destinationName));
		await using (var fileStream = File.Create(destination))
		{
			await file.CopyToAsync(fileStream, cancellationToken);
		}

		var (ingested, _) = await ingestion.ReconcileOszAsync(destination, cancellationToken);
		logger.LogInformation("Beatmapset created via admin API: IngestedCount={IngestedCount}", ingested);
		// No single canonical Location: the uploaded archive can hold more than one difficulty (and,
		// via ResolveBeatmapsetAsync's content-hash match, could even resolve to an existing set), so
		// this can't point at one specific created resource like a normal 201 would.
		return Results.Json(new IngestResult(ingested), statusCode: StatusCodes.Status201Created);
	}

	private static async Task<IResult> HandleGet(int beatmapsetId, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository,
		IBeatmapRepository beatmapRepository, CancellationToken cancellationToken)
	{
		var beatmapset = await beatmapsetRepository.FetchByIdAsync(beatmapsetId, cancellationToken);
		if (beatmapset is null) return Results.NotFound(new ErrorResponse("Beatmapset not found."));

		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		if (beatmapset.IsPrivate && !isAdmin) return Results.NotFound(new ErrorResponse("Beatmapset not found."));

		var beatmaps = await beatmapRepository.FetchAllBySetIdAsync(beatmapsetId, isAdmin, cancellationToken);
		return Results.Json(beatmapset.ToDetail(beatmaps));
	}

	private static async Task<IResult> HandleReplace(int beatmapsetId, HttpContext context,
		IBeatmapsetRepository beatmapsetRepository,
		IOptions<StorageOptions> storage, ILogger<BeatmapsetRoutesLog> logger, CancellationToken cancellationToken)
	{
		var beatmapset = await beatmapsetRepository.FetchByIdAsync(beatmapsetId, cancellationToken);
		if (beatmapset is null) return Results.NotFound(new ErrorResponse("Beatmapset not found."));
		if (beatmapset.IsFrozen)
			return Results.Conflict(new ErrorResponse("This beatmapset is frozen and cannot be modified."));

		if (!context.Request.HasFormContentType)
			return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

		IFormCollection form;
		try
		{
			form = await context.Request.ReadFormAsync(cancellationToken);
		}
		catch (InvalidDataException)
		{
			return Results.BadRequest(new ErrorResponse("Malformed multipart upload."));
		}

		var file = form.Files.GetFile("file");
		if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));
		if (!string.Equals(Path.GetExtension(file.FileName), ".osz", StringComparison.OrdinalIgnoreCase))
			return Results.BadRequest(new ErrorResponse("Only .osz uploads are accepted."));

		var targetFolder = BeatmapIngestionService.FindBeatmapsetFolder(storage.Value, beatmapsetId);
		if (targetFolder is null) return Results.NotFound(new ErrorResponse("Beatmapset not found."));

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

		logger.LogInformation("Beatmapset replace accepted via admin API: BeatmapsetId={BeatmapsetId}", beatmapsetId);
		return Results.Accepted(value: new BeatmapsetOperationAccepted(beatmapsetId, "replace"));
	}

	private static async Task<IResult> HandleDelete(int beatmapsetId, IBeatmapsetRepository beatmapsetRepository,
		IOptions<StorageOptions> storage, ILogger<BeatmapsetRoutesLog> logger, CancellationToken cancellationToken)
	{
		var beatmapset = await beatmapsetRepository.FetchByIdAsync(beatmapsetId, cancellationToken);
		if (beatmapset is null) return Results.NotFound(new ErrorResponse("Beatmapset not found."));
		if (beatmapset.IsFrozen)
			return Results.Conflict(new ErrorResponse("This beatmapset is frozen and cannot be deleted."));

		var folder = BeatmapIngestionService.FindBeatmapsetFolder(storage.Value, beatmapsetId);
		if (folder is null) return Results.NotFound(new ErrorResponse("Beatmapset not found."));

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

		logger.LogInformation("Beatmapset delete accepted via admin API: BeatmapsetId={BeatmapsetId}", beatmapsetId);
		return Results.Accepted(value: new BeatmapsetOperationAccepted(beatmapsetId, "delete"));
	}

	private static async Task<IResult> HandlePatch(int beatmapsetId, BeatmapsetPatchBody body,
		IBeatmapsetRepository beatmapsetRepository, IBeatmapRepository beatmapRepository,
		ILogger<BeatmapsetRoutesLog> logger,
		CancellationToken cancellationToken)
	{
		if (await beatmapsetRepository.FetchByIdAsync(beatmapsetId, cancellationToken) is null)
			return Results.NotFound(new ErrorResponse("Beatmapset not found."));

		if (body.Frozen is not null)
			await beatmapsetRepository.SetFrozenAsync(beatmapsetId, body.Frozen.Value, cancellationToken);
		if (body.Private is not null)
			await beatmapsetRepository.SetPrivateAsync(beatmapsetId, body.Private.Value, cancellationToken);
		logger.LogInformation(
			"Beatmapset updated via admin API: BeatmapsetId={BeatmapsetId} Frozen={Frozen} Private={Private}",
			beatmapsetId, body.Frozen, body.Private);

		var updated = await beatmapsetRepository.FetchByIdAsync(beatmapsetId, cancellationToken);
		var beatmaps = await beatmapRepository.FetchAllBySetIdAsync(beatmapsetId, true,
			cancellationToken);
		return Results.Json(updated!.ToDetail(beatmaps));
	}

	private static async Task<IResult> HandleBeatmapInfo(int beatmapsetId, int beatmapId, HttpContext context,
		IBeatmapRepository beatmaps, CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: beatmapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != beatmapsetId)
			return Results.NotFound(new ErrorResponse("Beatmap not found."));

		var siblings = await beatmaps.FetchAllBySetIdAsync(beatmapsetId, isAdmin, cancellationToken);
		var beatmapset = bmap.Beatmapset.ToSummary(siblings.Count);
		return Results.Json(bmap.ToDetail(beatmapset));
	}

	private static async Task<IResult> HandleBeatmapDifficulty(int beatmapsetId, int beatmapId,
		[FromQuery] int? mode, [FromQuery] uint? mods, HttpContext context, IBeatmapRepository beatmaps,
		IOsuCalculator calculator, IOptions<StorageOptions> storage, CancellationToken cancellationToken)
	{
		var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
		var bmap = await beatmaps.FetchOneAsync(beatmapId, setId: beatmapsetId, includePrivate: isAdmin,
			cancellationToken: cancellationToken);
		if (bmap is null || bmap.Beatmapset.Id != beatmapsetId)
			return Results.NotFound(new ErrorResponse("Beatmap not found."));

		if (mode is < 0 or > 3)
			return Results.BadRequest(new ErrorResponse(
				$"Unknown mode '{mode}'. Valid values: 0 (osu!), 1 (taiko), 2 (catch), 3 (mania)."));
		var resolvedMode = mode is { } m ? (GameMode)m : bmap.Difficulty.Mode;
		var resolvedMods = ((Mods)(mods ?? 0)).FilterInvalidCombos(resolvedMode);

		var osuPath = BeatmapIngestionService.OsuFilePath(storage.Value, bmap);
		if (!File.Exists(osuPath)) return Results.NotFound(new ErrorResponse("Beatmap file not found."));

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

		var siblings = await beatmaps.FetchAllBySetIdAsync(beatmapsetId, isAdmin, cancellationToken);
		var beatmapset = bmap.Beatmapset.ToSummary(siblings.Count);
		var detail = new BeatmapDetail(bmap.Md5, bmap.Id, bmap.Version, analysis.Difficulty,
			analysis.ObjectCounts, bmap.IsLocallyIngested, beatmapset);

		return Results.Json(new BeatmapDifficultyResult(resolvedMods, detail));
	}

	/// <summary>Request body for `PATCH /beatmapsets/{beatmapsetId}`: each field is applied only if present.</summary>
	public sealed record BeatmapsetPatchBody(bool? Frozen = null, bool? Private = null);

	private sealed record IngestResult(int BeatmapsProcessed);

	/// <summary>Response body for the asynchronous `PUT`/`DELETE /beatmapsets/{beatmapsetId}` 202 responses.</summary>
	public sealed record BeatmapsetOperationAccepted(int BeatmapsetId, string Operation);

	/// <summary>
	///     Response for `GET /beatmapsets/{beatmapsetId}/{beatmapId}/difficulty`. The `mods` field echoes the
	///     actually applied mod combination, which can differ from the raw `mods` query param the caller sent
	///     because conflicting pairs like `EZ+HR` are silently resolved.
	/// </summary>
	public sealed record BeatmapDifficultyResult(Mods Mods, BeatmapDetail Beatmap);
}