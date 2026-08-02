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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing;

/// <summary>
///     Dedicated <c>ILogger&lt;T&gt;</c> category marker, because <see cref="BeatmapsetRoutes" /> is static and
///     can't be a type argument.
/// </summary>
internal sealed class BeatmapsetRoutesLog;

/// <summary>
///     `/beatmapsets`: resource-oriented routes replacing the admin-only `/beatmaps` search/upload
///     surface plus the old bare `GET /beatmapset/{id}`. Reads are public, with a soft admin-only
///     elevation (a private beatmapset's beatmaps become visible); every write is admin-key gated.
///     `PUT`/`DELETE` are filesystem-first and asynchronous (202 Accepted, never touch the database
///     directly): the live <see cref="BeatmapWatcherService" /> reconciles the database from the
///     resulting filesystem change within its own debounce window. See
///     <see cref="BeatmapIngestionService.DeletedFolderInfix" /> for how delete's atomic rename-in-place
///     is recognized as "gone" before the physical folder is actually reclaimed.
/// </summary>
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
			.WithSummary("List Beatmapsets")
			.WithDescription("Query params: `page` (default 1), `pageSize` (default 50). A private beatmapset " +
			                 "is excluded entirely unless the caller carries a valid `X-Admin-Key`. Response: " +
			                 "`{ page, pageSize, totalRecords, items }`, wrapped in the enveloped `meta` object at the " +
			                 "top level. Public.")
			.WithTags("Beatmapsets")
			.Produces<PagedResult<BeatmapsetSummary>>()
			.WithExample(StatusCodes.Status200OK, new PagedResult<BeatmapsetSummary>(1, 50, 1, [SampleSummary()]));

		group.MapPost("/beatmapsets", HandleCreate)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("createBeatmapset")
			.WithSummary("Create Beatmapset")
			.WithDescription("Multipart upload, field name `file`, must be a `.osz` archive. A lone `.osu` " +
			                 "file has no set context under this server's folder-per-beatmapset storage model. Runs a full " +
			                 "ingestion reconciliation pass synchronously and returns `{ ingested }` (the number of " +
			                 "beatmaps added/updated)." + AdminKeyNote)
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
			.WithSummary("Get Beatmapset Details")
			.WithDescription("Returns `{ id, artist, title, creator, lastUpdate, createdAt, isFrozen, " +
			                 "isPrivate, status, beatmaps }`. `beatmaps` is the full list of difficulties under this " +
			                 "set, each a `BeatmapInSet` (no parent beatmapset embed, unlike `GET " +
			                 "/beatmapsets/{mapsetId}/{beatmapId}`'s `BeatmapDetail`, to avoid the cycle). 404 if the " +
			                 "beatmapset doesn't exist, or (for a non-admin caller) it's private. Public, with a soft admin " +
			                 "elevation.")
			.WithTags("Beatmapsets")
			.Produces<BeatmapsetDetail>()
			.WithExample(StatusCodes.Status200OK, SampleDetail())
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPut("/beatmapsets/{mapsetId:int}", HandleReplace)
			.RequireAuthorization(AdminKeyDefaults.Policy)
			.WithGroupName("basilapi")
			.WithName("replaceBeatmapset")
			.WithSummary("Replace Beatmapset")
			.WithDescription("Multipart upload, field name `file`, must be a `.osz` archive. Filesystem-only " +
			                 "and asynchronous: extracts the new archive's contents directly into the beatmapset's existing " +
			                 "storage folder (overwriting files), then returns `202 Accepted` with a small body describing " +
			                 "what was accepted immediately. The database catches up shortly after via the same live " +
			                 "reconciliation the filesystem watcher already runs, not synchronously in this request. 404 " +
			                 "if the beatmapset doesn't exist; 409 if it's frozen (see `PATCH /beatmapsets/{mapsetId}`)." +
			                 AdminKeyNote)
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
			.WithSummary("Delete Beatmapset")
			.WithDescription("Filesystem-only and asynchronous: atomically renames the beatmapset's storage " +
			                 "folder in place (a TOCTOU-safe marker the live reconciliation and a background garbage " +
			                 "collector both recognize as \"gone\"), then returns `202 Accepted` with a small body " +
			                 "describing what was accepted. The database row and the physical folder are both cleaned " +
			                 "up shortly after, not synchronously in this request. 404 if the beatmapset doesn't exist; 409 " +
			                 "(folder left untouched) if the rename itself fails (e.g. a locked file) or if the beatmapset is " +
			                 "frozen (see `PATCH /beatmapsets/{mapsetId}`)." +
			                 AdminKeyNote)
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
			.WithSummary("Update Beatmapset")
			.WithDescription("Body: `{ frozen?, private? }`. Each field is applied only if present. " +
			                 "`frozen` is a write-lock: while set, `PUT`/`DELETE /beatmapsets/{mapsetId}` are " +
			                 "rejected with 409 regardless of admin role (this route itself is exempt, so unfreezing is " +
			                 "always possible). `private` hides the beatmapset (and every beatmap under it) from non-admin " +
			                 "listings/lookups. Returns the updated beatmapset info (same shape as `GET`). 404 if the " +
			                 "beatmapset doesn't exist." + AdminKeyNote)
			.WithTags("Beatmapsets")
			.Produces<BeatmapsetDetail>()
			.WithExample(StatusCodes.Status200OK, SampleDetail() with { IsFrozen = true })
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}", HandleBeatmapInfo)
			.WithGroupName("basilapi")
			.WithName("getBeatmap")
			.WithSummary("Get Beatmap Details")
			.WithDescription("Returns a `BeatmapDetail` (difficulty/object-count metadata plus the parent " +
			                 "`beatmapset` embed), unlike each entry of `GET /beatmapsets/{mapsetId}`'s `beatmaps` list " +
			                 "(a `BeatmapInSet`, no parent embed, to avoid the beatmap-in-set-in-beatmap cycle). Never " +
			                 "includes the internal filename/background-image filename (see `GET .../background` " +
			                 "instead). 404 if the beatmap doesn't exist, doesn't belong to this beatmapset, or the parent " +
			                 "beatmapset is private and the caller isn't admin. Public, with a soft admin elevation.")
			.WithTags("Beatmaps")
			.Produces<BeatmapDetail>()
			.WithExample(StatusCodes.Status200OK, SampleBeatmap().ToDetail(SampleSummary()))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/difficulty", HandleBeatmapDifficulty)
			.WithGroupName("basilapi")
			.WithName("getBeatmapDifficulty")
			.WithSummary("Get Beatmap Difficulty (with mods)")
			.WithDescription("Recomputes star rating, BPM, length, and CS/AR/OD/HP for the given `mods` " +
			                 "bitflag query param (default `0`, no mods) and `mode` query param (default: the " +
			                 "beatmap's own mode; `0`=osu!, `1`=taiko, `2`=catch, `3`=mania). HR/EZ's CS/AR/OD/HP " +
			                 "multiplier and DT/HT/NC's BPM/length rate scaling apply in every mode; DT/HT/NC's " +
			                 "AR/OD time-window shift is osu!std-only (AR/OD pass through unadjusted in " +
			                 "taiko/catch/mania, since each ruleset defines its own hit-window formula, not " +
			                 "implemented here). Invalid mod combinations (e.g. `EZ+HR`) are silently resolved the " +
			                 "same way multiplayer room mods are — the response's `mods` field echoes what was " +
			                 "actually applied. 400 if `mode` is out of range or the beatmap can't be analyzed " +
			                 "under the requested ruleset. 404 if the beatmap doesn't exist, doesn't belong to " +
			                 "this beatmapset, its file is missing on disk, or the parent beatmapset is private and the " +
			                 "caller isn't admin. Public, with a soft admin elevation.")
			.WithTags("Beatmaps")
			.Produces<BeatmapDifficultyResult>()
			.WithExample(StatusCodes.Status200OK,
				new BeatmapDifficultyResult(Mods.NoMod, SampleBeatmap().ToDetail(SampleSummary())))
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/download", HandleDownloadBeatmap)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmap")
			.WithSummary("Download Beatmap")
			.WithDescription("Serves the raw `.osu` difficulty file. 404 if the beatmap doesn't exist, " +
			                 "doesn't belong to this beatmapset, its file is missing on disk, or the parent beatmapset is " +
			                 "private and the caller isn't admin. Content-Type `application/x-osu-beatmap`. Public, " +
			                 "with a soft admin elevation.")
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/background", HandleDownloadBackground)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapBackground")
			.WithSummary("Download Beatmap Background")
			.WithDescription("Serves the beatmap's background image file. 404 if the beatmap doesn't exist, " +
			                 "doesn't belong to this beatmapset, has no recorded background image, its file is missing on " +
			                 "disk, or the parent beatmapset is private and the caller isn't admin. Content-Type inferred " +
			                 "from the file extension. Public, with a soft admin elevation.")
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/background", HandleDownloadMapsetBackground)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapsetBackground")
			.WithSummary("Download Beatmapset Background")
			.WithDescription("Serves the preview background image for this set (the lowest-id " +
			                 "beatmap's background), kept in sync by ingestion. 404 if the beatmapset doesn't exist, has no " +
			                 "recorded preview background, its file is missing on disk, or the beatmapset is private and the " +
			                 "caller isn't admin. Content-Type inferred from the file extension. Public, with a soft " +
			                 "admin elevation.")
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/audio", HandleDownloadAudio)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapAudio")
			.WithSummary("Download Beatmap Audio")
			.WithDescription("Serves the beatmap's audio file. 404 if the beatmap doesn't exist, doesn't " +
			                 "belong to this beatmapset, has no recorded audio file, its file is missing on disk, or the " +
			                 "parent beatmapset is private and the caller isn't admin. Content-Type inferred from the file " +
			                 "extension. Public, with a soft admin elevation.")
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/audio", HandleDownloadMapsetAudio)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapsetAudio")
			.WithSummary("Download Beatmapset Audio")
			.WithDescription("Serves the preview audio file for this set (the lowest-id beatmap's audio " +
			                 "file), kept in sync by ingestion. 404 if the beatmapset doesn't exist, has no recorded audio " +
			                 "file, its file is missing on disk, or the beatmapset is private and the caller isn't admin. " +
			                 "Content-Type inferred from the file extension. Public, with a soft admin elevation.")
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/video", HandleDownloadVideo)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapVideo")
			.WithSummary("Download Beatmap Video")
			.WithDescription("Serves the video file declared in the beatmap's `.osu` `[Events]` section " +
			                 "(found via osu!'s own storyboard decoder, the same module background-finding relies " +
			                 "on — not a stored column, decoded fresh on every request). 404 if the beatmap doesn't " +
			                 "exist, doesn't belong to this beatmapset, its `.osu` declares no video, the video file is " +
			                 "missing on disk, or the parent beatmapset is private and the caller isn't admin. " +
			                 "Content-Type inferred from the file extension. Public, with a soft admin elevation.")
			.WithTags("Beatmaps")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/audiopreview", HandleAudioPreview)
			.WithGroupName("basilapi")
			.WithName("getBeatmapsetAudioPreview")
			.WithSummary("Get Beatmapset Audio Preview")
			.WithDescription("Serves a 10-second mp3 clip (128kbps) cut from the beatmapset's preview beatmap's " +
			                 "audio file, starting at its recorded PreviewTime — the same clip and cache entry as " +
			                 "the `b.` host's `/preview/{mapsetId}.mp3`. 404 if the beatmapset doesn't exist, is private, " +
			                 "or has no audio file on disk.")
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/storyboard", HandleDownloadStoryboard)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapsetStoryboard")
			.WithSummary("Download Beatmapset Storyboard")
			.WithDescription("Serves the beatmapset folder's `.osb` storyboard file. A beatmapset is expected to " +
			                 "carry at most one; if more than one is somehow present, the first in filename order is " +
			                 "served. 404 if the beatmapset has no local folder, or the folder has no `.osb` file at all. " +
			                 "Content-Type `application/x-osu-storyboard`. Public, no admin key.")
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/beatmapsets/{mapsetId:int}/download", HandleDownloadArchive)
			.WithGroupName("basilapi")
			.WithName("downloadBeatmapset")
			.WithSummary("Download Beatmapset")
			.WithDescription("Builds a fresh `.osz` on the fly from the beatmapset's local storage folder (every " +
			                 "file in the folder: audio, images, video, every `.osu`/`.osb`) and serves it. 404 if the " +
			                 "beatmapset has no local folder, or the folder is empty. Content-Type " +
			                 "`application/x-osu-beatmap-archive`. Public, no admin key.")
			.WithTags("Beatmapsets")
			.ProducesProblem(StatusCodes.Status404NotFound);
	}

	private static BeatmapsetSummary SampleSummary()
	{
		var created = DateTime.Parse("2026-06-01T10:00:00Z");
		return new BeatmapsetSummary(321, "Camellia", "Exit This Earth's Atmosphere", "RLC", created, created,
			false, false, BeatmapStatus.Loved, 1);
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
				$"Beatmap can't be analyzed as mode {resolvedMode} — likely an unsupported ruleset conversion."));
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
		return File.Exists(osuPath) ? Results.File(osuPath, "application/x-osu-beatmap") : Results.NotFound();
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
		return Path.GetExtension(path).ToLowerInvariant() switch
		{
			".png" => "image/png",
			".gif" => "image/gif",
			".bmp" => "image/bmp",
			_ => "image/jpeg"
		};
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
		return Path.GetExtension(path).ToLowerInvariant() switch
		{
			".ogg" => "audio/ogg",
			".wav" => "audio/wav",
			_ => "audio/mpeg"
		};
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
		return Path.GetExtension(path).ToLowerInvariant() switch
		{
			".avi" => "video/x-msvideo",
			".flv" => "video/x-flv",
			".webm" => "video/webm",
			".mp4" => "video/mp4",
			_ => "application/octet-stream"
		};
	}

	private static async Task<IResult> HandleAudioPreview(int mapsetId, IBeatmapRepository beatmaps,
		IBeatmapsetRepository beatmapsetRepository, IOptions<StorageOptions> storage, IResponseCache cache,
		IAudioPreviewExtractor extractor, CancellationToken cancellationToken)
	{
		var clip = await BanchoHostGroups.GetOrGeneratePreviewClipAsync(mapsetId, beatmaps, beatmapsetRepository,
			storage, cache,
			extractor, cancellationToken);
		return clip is null ? Results.NotFound() : Results.File(clip, "audio/mpeg");
	}

	private static IResult HandleDownloadStoryboard(int mapsetId, IOptions<StorageOptions> storage)
	{
		var folder = BeatmapIngestionService.FindMapsetFolder(storage.Value, mapsetId);
		if (folder is null) return Results.NotFound();

		var osbPath = Directory.EnumerateFiles(folder, "*.osb").Order().FirstOrDefault();
		return osbPath is null ? Results.NotFound() : Results.File(osbPath, "application/x-osu-storyboard");
	}

	private static async Task<IResult> HandleDownloadArchive(int mapsetId, IBeatmapRepository beatmaps,
		IOptions<StorageOptions> storage, CancellationToken cancellationToken)
	{
		var osz = await BanchoHostGroups.BuildOszArchiveAsync(beatmaps, storage.Value, mapsetId, false,
			cancellationToken);
		return osz is null
			? Results.NotFound()
			: Results.File(osz.Value.Bytes, "application/x-osu-beatmap-archive", osz.Value.FileName);
	}

	/// <summary>Body for `PATCH /beatmapsets/{mapsetId}`: each field is applied only if present.</summary>
	public sealed record BeatmapsetPatchBody(bool? Frozen, bool? Private);

	private sealed record IngestResult(int Ingested);

	/// <summary>Body for the async, filesystem-first `PUT`/`DELETE /beatmapsets/{mapsetId}` 202 responses.</summary>
	public sealed record MapsetOperationAccepted(int MapsetId, string Operation);

	/// <summary>
	///     Response for `GET /beatmapsets/{mapsetId}/{beatmapId}/difficulty`: <see cref="Mods" /> echoes the
	///     actually-applied mod combination (after <see cref="ModsExtensions.FilterInvalidCombos" /> resolves any
	///     conflicting pair like `EZ+HR`), since it can differ from the raw `mods` query param the caller sent.
	/// </summary>
	public sealed record BeatmapDifficultyResult(Mods Mods, BeatmapDetail Beatmap);
}