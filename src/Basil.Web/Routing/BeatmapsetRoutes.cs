using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps;
using Basil.Web.Auth;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing;

/// <summary>
///     `/beatmapsets` — resource-oriented routes replacing the admin-only `/beatmaps` search/upload
///     surface plus the old bare `GET /mapset/{id}`. Reads are public, with a soft admin-only
///     elevation (a private mapset's beatmaps become visible); every write is admin-key gated.
///     `PUT`/`DELETE` are filesystem-first and asynchronous (202 Accepted, never touch the database
///     directly) — the live <see cref="BeatmapWatcherService" /> reconciles the database from the
///     resulting filesystem change within its own debounce window. See
///     <see cref="BeatmapIngestionService.DeletedFolderInfix" /> for how delete's atomic rename-in-place
///     is recognized as "gone" before the physical folder is actually reclaimed.
/// </summary>
internal static class BeatmapsetRoutes
{
    public static void MapBeatmapsetRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/beatmapsets", HandleList)
            .WithGroupName("basilapi")
            .WithName("listBeatmapsets")
            .WithSummary("List Beatmapsets")
            .WithDescription("Query params: `page` (default 1), `pageSize` (default 50). A private mapset " +
                "is excluded entirely unless the caller carries a valid `X-Admin-Key`. Response: " +
                "`{ page, pageSize, count, hasMore, items }`. Public.")
            .WithTags("Beatmapsets")
            .Produces<PagedResult<BeatmapsetSummary>>()
            .WithExample(StatusCodes.Status200OK, new PagedResult<BeatmapsetSummary>(1, 50, 1, false,
                [SampleSummary()]));

        group.MapPost("/beatmapsets", HandleCreate)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("createBeatmapset")
            .WithSummary("Create Beatmapset")
            .WithDescription("Multipart upload, field name `file`, must be a `.osz` archive — a lone `.osu` " +
                "file has no set context under this server's folder-per-mapset storage model. Runs a full " +
                "ingestion reconciliation pass synchronously and returns `{ ingested }` (the number of " +
                "beatmaps added/updated)." + AdminKeyNote)
            .WithTags("Beatmapsets")
            .Produces<IngestResult>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status200OK, new IngestResult(5))
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Only .osz uploads are accepted — a single .osu file has no set context."));

        group.MapGet("/beatmapsets/{mapsetId:int}", HandleGet)
            .WithGroupName("basilapi")
            .WithName("getBeatmapset")
            .WithSummary("Get Beatmapset")
            .WithDescription("Returns `{ id, artist, title, creator, createdAt, lastUpdate, isFrozen, " +
                "isPrivate, beatmaps }` — `beatmaps` is the full list of difficulties under this set, each " +
                "the real domain `Beatmap` object (nested `Mapset`/`Difficulty`, same shape `GET " +
                "/beatmapsets/{mapsetId}/{beatmapId}` returns for one). 404 if the mapset doesn't exist, or " +
                "(for a non-admin caller) it's private. Public, with a soft admin elevation.")
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
                "and asynchronous: extracts the new archive's contents directly into the mapset's existing " +
                "storage folder (overwriting files), then returns `202 Accepted` immediately — the database " +
                "catches up shortly after via the same live reconciliation the filesystem watcher already " +
                "runs, not synchronously in this request. 404 if the mapset doesn't exist; 409 if it's " +
                "frozen (see `PATCH /beatmapsets/{mapsetId}`)." + AdminKeyNote)
            .WithTags("Beatmapsets")
            .Produces(StatusCodes.Status202Accepted)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Only .osz uploads are accepted."))
            .WithExample(StatusCodes.Status409Conflict, new ErrorResponse("This mapset is frozen and cannot be modified."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/beatmapsets/{mapsetId:int}", HandleDelete)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("deleteBeatmapset")
            .WithSummary("Delete Beatmapset")
            .WithDescription("Filesystem-only and asynchronous: atomically renames the mapset's storage " +
                "folder in place (a TOCTOU-safe marker the live reconciliation and a background garbage " +
                "collector both recognize as \"gone\"), then returns `202 Accepted` — the database row and " +
                "the physical folder are both cleaned up shortly after, not synchronously in this request. " +
                "404 if the mapset doesn't exist; 409 (folder left untouched) if the rename itself fails " +
                "(e.g. a locked file) or if the mapset is frozen (see `PATCH /beatmapsets/{mapsetId}`)." +
                AdminKeyNote)
            .WithTags("Beatmapsets")
            .Produces(StatusCodes.Status202Accepted)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithExample(StatusCodes.Status409Conflict, new ErrorResponse("This mapset is frozen and cannot be deleted."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/beatmapsets/{mapsetId:int}", HandlePatch)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithName("updateBeatmapset")
            .WithSummary("Update Beatmapset")
            .WithDescription("Body: `{ frozen?, private? }` — each field is applied only if present. " +
                "`frozen` is a write-lock: while set, `PUT`/`DELETE /beatmapsets/{mapsetId}` are " +
                "rejected with 409 regardless of admin role (this route itself is exempt, so unfreezing is " +
                "always possible). `private` hides the mapset (and every beatmap under it) from non-admin " +
                "listings/lookups. Returns the updated beatmapset info (same shape as `GET`). 404 if the " +
                "mapset doesn't exist." + AdminKeyNote)
            .WithTags("Beatmapsets")
            .Produces<BeatmapsetDetail>()
            .WithExample(StatusCodes.Status200OK, SampleDetail() with { IsFrozen = true })
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}", HandleBeatmapInfo)
            .WithGroupName("basilapi")
            .WithName("getBeatmap")
            .WithSummary("Get Beatmap")
            .WithDescription("Returns the real domain `Beatmap` object directly (nested `Mapset`/`Difficulty`, " +
                "per-mode `objectCounts`, `length`) — the same shape each entry of `GET " +
                "/beatmapsets/{mapsetId}`'s `beatmaps` list uses. Never includes the internal background-" +
                "image filename (see `GET .../background` instead). 404 if the beatmap doesn't exist, " +
                "doesn't belong to this mapset, or the parent mapset is private and the caller isn't admin. " +
                "Public, with a soft admin elevation.")
            .WithTags("Beatmapsets")
            .Produces<Beatmap>()
            .WithExample(StatusCodes.Status200OK, SampleBeatmap())
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/download", HandleDownloadBeatmap)
            .WithGroupName("basilapi")
            .WithName("downloadBeatmap")
            .WithSummary("Download Beatmap")
            .WithDescription("Serves the raw `.osu` difficulty file. 404 if the beatmap doesn't exist, " +
                "doesn't belong to this mapset, its file is missing on disk, or the parent mapset is " +
                "private and the caller isn't admin. Content-Type `application/x-osu-beatmap`. Public, " +
                "with a soft admin elevation.")
            .WithTags("Beatmapsets")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/beatmapsets/{mapsetId:int}/{beatmapId:int}/background", HandleDownloadBackground)
            .WithGroupName("basilapi")
            .WithName("downloadBeatmapBackground")
            .WithSummary("Download Beatmap Background")
            .WithDescription("Serves the beatmap's background image file. 404 if the beatmap doesn't exist, " +
                "doesn't belong to this mapset, has no recorded background image, its file is missing on " +
                "disk, or the parent mapset is private and the caller isn't admin. Content-Type inferred " +
                "from the file extension. Public, with a soft admin elevation.")
            .WithTags("Beatmapsets")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/beatmapsets/{mapsetId:int}/storyboard", HandleDownloadStoryboard)
            .WithGroupName("basilapi")
            .WithName("downloadBeatmapsetStoryboard")
            .WithSummary("Download Beatmapset Storyboard")
            .WithDescription("Serves the mapset folder's `.osb` storyboard file. A mapset is expected to " +
                "carry at most one; if more than one is somehow present, the first in filename order is " +
                "served. 404 if the mapset has no local folder, or the folder has no `.osb` file at all. " +
                "Content-Type `application/x-osu-storyboard`. Public, no admin key.")
            .WithTags("Beatmapsets")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/beatmapsets/{mapsetId:int}/download", HandleDownloadArchive)
            .WithGroupName("basilapi")
            .WithName("downloadBeatmapset")
            .WithSummary("Download Beatmapset")
            .WithDescription("Builds a fresh `.osz` on the fly from the mapset's local storage folder (every " +
                "file in the folder — audio, images, video, every `.osu`/`.osb`) and serves it. 404 if the " +
                "mapset has no local folder, or the folder is empty. Content-Type " +
                "`application/x-osu-beatmap-archive`. Public, no admin key.")
            .WithTags("Beatmapsets")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static BeatmapsetSummary SampleSummary()
    {
        var created = DateTime.Parse("2026-06-01T10:00:00Z");
        return new BeatmapsetSummary(321, "Camellia", "Exit This Earth's Atmosphere", "RLC", created, created,
            false, false);
    }

    private static BeatmapsetDetail SampleDetail()
    {
        var s = SampleSummary();
        return new BeatmapsetDetail(s.Id, s.Artist, s.Title, s.Creator, s.CreatedAt, s.LastUpdate, s.IsFrozen,
            s.IsPrivate, [SampleBeatmap()]);
    }

    private static Beatmap SampleBeatmap()
    {
        var created = DateTime.Parse("2026-06-01T10:00:00Z");
        var mapset = new Mapset(321, "Camellia", "Exit This Earth's Atmosphere", "RLC", created, created);
        var difficulty = new Difficulty(GameMode.Standard, 174, 4, 9, 8, 6, 6.42);
        return new Beatmap("d41d8cd98f00b204e9800998ecf8427e", 654, mapset, "Extreme",
            "camellia - exit this earth's atmosphere (rlc) [extreme].osu", TimeSpan.FromSeconds(225), 1234, 57, 12,
            difficulty, new Dictionary<string, int> { ["circle"] = 620, ["slider"] = 210, ["spinner"] = 2 });
    }

    private const string AdminKeyNote = RouteDocs.AdminKeyNote;

    private sealed record BeatmapsetSummary(int Id, string Artist, string Title, string Creator, DateTime CreatedAt,
        DateTime LastUpdate, bool IsFrozen, bool IsPrivate);

    private sealed record BeatmapsetDetail(int Id, string Artist, string Title, string Creator, DateTime CreatedAt,
        DateTime LastUpdate, bool IsFrozen, bool IsPrivate, IReadOnlyList<Beatmap> Beatmaps);

    public sealed record BeatmapsetPatchBody(bool? Frozen, bool? Private);

    private sealed record IngestResult(int Ingested);

    private static async Task<IResult> HandleList([FromQuery] int? page, [FromQuery] int? pageSize,
        HttpContext context, IMapsetRepository mapsets, CancellationToken cancellationToken)
    {
        var (p, ps) = Pagination.Normalize(page, pageSize);
        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);

        var overqueried = await mapsets.FetchPageAsync((p - 1) * ps, ps + 1, !isAdmin, cancellationToken);
        var items = overqueried
            .Select(m => new BeatmapsetSummary(m.Id, m.Artist, m.Title, m.Creator, m.CreatedAt, m.LastUpdate,
                m.IsFrozen, m.IsPrivate))
            .ToList();

        return Results.Json(Pagination.Trim(items, p, ps));
    }

    private static async Task<IResult> HandleCreate(HttpContext context, IOptions<StorageOptions> storage,
        BeatmapIngestionService ingestion, CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".osz", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new ErrorResponse("Only .osz uploads are accepted — a single .osu file has no set context."));

        Directory.CreateDirectory(storage.Value.MapsetsPath);
        var destinationName = $"{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(storage.Value.MapsetsPath, Path.GetFileName(destinationName));
        await using (var fileStream = File.Create(destination))
        {
            await file.CopyToAsync(fileStream, cancellationToken);
        }

        var ingested = await ingestion.ReconcileAllAsync(cancellationToken);
        return Results.Json(new IngestResult(ingested));
    }

    private static async Task<IResult> HandleGet(int mapsetId, HttpContext context, IMapsetRepository mapsets,
        IMapRepository maps, CancellationToken cancellationToken)
    {
        var mapset = await mapsets.FetchByIdAsync(mapsetId, cancellationToken);
        if (mapset is null) return Results.NotFound();

        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
        if (mapset.IsPrivate && !isAdmin) return Results.NotFound();

        var beatmaps = await maps.FetchAllBySetIdAsync(mapsetId, isAdmin, cancellationToken);
        return Results.Json(BuildDetail(mapset, beatmaps));
    }

    private static BeatmapsetDetail BuildDetail(Mapset mapset, IReadOnlyList<Beatmap> beatmaps)
    {
        return new BeatmapsetDetail(mapset.Id, mapset.Artist, mapset.Title, mapset.Creator, mapset.CreatedAt,
            mapset.LastUpdate, mapset.IsFrozen, mapset.IsPrivate, beatmaps);
    }

    private static async Task<IResult> HandleReplace(int mapsetId, HttpContext context, IMapsetRepository mapsets,
        IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var mapset = await mapsets.FetchByIdAsync(mapsetId, cancellationToken);
        if (mapset is null) return Results.NotFound();
        if (mapset.IsFrozen) return Results.Conflict(new ErrorResponse("This mapset is frozen and cannot be modified."));

        if (!context.Request.HasFormContentType) return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));
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

        return Results.Accepted();
    }

    private static async Task<IResult> HandleDelete(int mapsetId, IMapsetRepository mapsets,
        IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var mapset = await mapsets.FetchByIdAsync(mapsetId, cancellationToken);
        if (mapset is null) return Results.NotFound();
        if (mapset.IsFrozen) return Results.Conflict(new ErrorResponse("This mapset is frozen and cannot be deleted."));

        var folder = BeatmapIngestionService.FindMapsetFolder(storage.Value, mapsetId);
        if (folder is null) return Results.NotFound();

        var deletedFolder = folder + BeatmapIngestionService.DeletedFolderInfix + Guid.NewGuid().ToString("N");
        try
        {
            Directory.Move(folder, deletedFolder);
        }
        catch (IOException)
        {
            return Results.Conflict(new ErrorResponse("The mapset's files are currently in use; try again shortly."));
        }

        return Results.Accepted();
    }

    private static async Task<IResult> HandlePatch(int mapsetId, BeatmapsetPatchBody body,
        IMapsetRepository mapsets, IMapRepository maps, CancellationToken cancellationToken)
    {
        if (await mapsets.FetchByIdAsync(mapsetId, cancellationToken) is null) return Results.NotFound();

        if (body.Frozen is not null) await mapsets.SetFrozenAsync(mapsetId, body.Frozen.Value, cancellationToken);
        if (body.Private is not null) await mapsets.SetPrivateAsync(mapsetId, body.Private.Value, cancellationToken);

        var updated = await mapsets.FetchByIdAsync(mapsetId, cancellationToken);
        var beatmaps = await maps.FetchAllBySetIdAsync(mapsetId, includePrivate: true,
            cancellationToken: cancellationToken);
        return Results.Json(BuildDetail(updated!, beatmaps));
    }

    private static async Task<IResult> HandleBeatmapInfo(int mapsetId, int beatmapId, HttpContext context,
        IMapRepository maps, CancellationToken cancellationToken)
    {
        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
        var bmap = await maps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
            cancellationToken: cancellationToken);
        if (bmap is null || bmap.Mapset.Id != mapsetId) return Results.NotFound();

        return Results.Json(bmap);
    }

    private static async Task<IResult> HandleDownloadBeatmap(int mapsetId, int beatmapId, HttpContext context,
        IMapRepository maps, IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
        var bmap = await maps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
            cancellationToken: cancellationToken);
        if (bmap is null || bmap.Mapset.Id != mapsetId) return Results.NotFound();

        var osuPath = BeatmapIngestionService.OsuFilePath(storage.Value, bmap);
        return File.Exists(osuPath) ? Results.File(osuPath, "application/x-osu-beatmap") : Results.NotFound();
    }

    private static async Task<IResult> HandleDownloadBackground(int mapsetId, int beatmapId, HttpContext context,
        IMapRepository maps, IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
        var bmap = await maps.FetchOneAsync(beatmapId, setId: mapsetId, includePrivate: isAdmin,
            cancellationToken: cancellationToken);
        if (bmap is null || bmap.Mapset.Id != mapsetId) return Results.NotFound();

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

    private static IResult HandleDownloadStoryboard(int mapsetId, IOptions<StorageOptions> storage)
    {
        var folder = BeatmapIngestionService.FindMapsetFolder(storage.Value, mapsetId);
        if (folder is null) return Results.NotFound();

        var osbPath = Directory.EnumerateFiles(folder, "*.osb").Order().FirstOrDefault();
        return osbPath is null ? Results.NotFound() : Results.File(osbPath, "application/x-osu-storyboard");
    }

    private static async Task<IResult> HandleDownloadArchive(int mapsetId, IMapRepository maps,
        IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var osz = await BanchoHostGroups.BuildOszArchiveAsync(maps, storage.Value, mapsetId, false, cancellationToken);
        return osz is null
            ? Results.NotFound()
            : Results.File(osz.Value.Bytes, "application/x-osu-beatmap-archive", osz.Value.FileName);
    }
}
