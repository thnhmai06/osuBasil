using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps;
using Basil.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing;

/// <summary>
///     `/mapset` — resource-oriented routes replacing the admin-only `/beatmaps` search/upload surface
///     plus the old bare `GET /mapset/{id}`. Reads are public, with a soft admin-only elevation
///     (frozen/private beatmaps become visible); every write is admin-key gated. `PUT`/`DELETE` are
///     filesystem-first and asynchronous (202 Accepted, never touch the database directly) — the live
///     <see cref="BeatmapWatcherService" /> reconciles the database from the resulting filesystem
///     change within its own debounce window. See <see cref="BeatmapIngestionService.DeletedFolderInfix" />
///     for how delete's atomic rename-in-place is recognized as "gone" before the physical folder is
///     actually reclaimed.
/// </summary>
internal static class MapsetRoutes
{
    public static void MapMapsetRoutes(this RouteGroupBuilder group)
    {
        group.MapGet("/mapset", HandleList)
            .WithGroupName("basilapi")
            .WithSummary("List mapsets, paged.")
            .WithDescription("Query params: `page` (default 1), `pageSize` (default 50). A mapset whose " +
                "every beatmap is private is excluded entirely unless the caller carries a valid " +
                "`X-Admin-Key`. Response: `{ page, pageSize, count, hasMore, items }`. Public.")
            .WithTags("Beatmap Downloads");

        group.MapPost("/mapset", HandleCreate)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Upload a beatmap set (.osz).")
            .WithDescription("Multipart upload, field name `file`, must be a `.osz` archive — a lone `.osu` " +
                "file has no set context under this server's folder-per-mapset storage model. Runs a full " +
                "ingestion reconciliation pass synchronously and returns `{ ingested }` (the number of " +
                "beatmaps added/updated)." + AdminKeyNote)
            .WithTags("Beatmap Downloads");

        group.MapGet("/mapset/{id:int}", HandleGet)
            .WithGroupName("basilapi")
            .WithSummary("Get one mapset's info, by mapset id.")
            .WithDescription("Returns `{ id, artist, title, creator, createdAt, lastUpdate, isFrozen, " +
                "beatmaps: [{ id, version, mode }] }` — beatmap ids are included inline so a client doesn't " +
                "need a second call to discover them. 404 if the mapset doesn't exist, or (for a non-admin " +
                "caller) every one of its beatmaps is private. Public, with a soft admin elevation.")
            .WithTags("Beatmap Downloads");

        group.MapPut("/mapset/{id:int}", HandleReplace)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Replace a mapset's archive (re-ingest), asynchronously.")
            .WithDescription("Multipart upload, field name `file`, must be a `.osz` archive. Filesystem-only " +
                "and asynchronous: extracts the new archive's contents directly into the mapset's existing " +
                "storage folder (overwriting files), then returns `202 Accepted` immediately — the database " +
                "catches up shortly after via the same live reconciliation the filesystem watcher already " +
                "runs, not synchronously in this request. 404 if the mapset doesn't exist; 409 if it's " +
                "frozen (see `PATCH /mapset/{id}/freeze`)." + AdminKeyNote)
            .WithTags("Beatmap Downloads");

        group.MapDelete("/mapset/{id:int}", HandleDelete)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Delete a mapset, asynchronously.")
            .WithDescription("Filesystem-only and asynchronous: atomically renames the mapset's storage " +
                "folder in place (a TOCTOU-safe marker the live reconciliation and a background garbage " +
                "collector both recognize as \"gone\"), then returns `202 Accepted` — the database row and " +
                "the physical folder are both cleaned up shortly after, not synchronously in this request. " +
                "404 if the mapset doesn't exist; 409 (folder left untouched) if the rename itself fails " +
                "(e.g. a locked file) or if the mapset is frozen (see `PATCH /mapset/{id}/freeze`)." +
                AdminKeyNote)
            .WithTags("Beatmap Downloads");

        group.MapPatch("/mapset/{id:int}/freeze", HandleFreeze)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Freeze or unfreeze a mapset (a write-lock).")
            .WithDescription("Body: `{ frozen }`. While frozen, `PUT`/`DELETE /mapset/{id}` are rejected " +
                "with 409 regardless of admin role; this route itself is exempt from that lock, so " +
                "unfreezing is always possible. Returns `{ id, isFrozen }`. 404 if the mapset doesn't exist." +
                AdminKeyNote)
            .WithTags("Beatmap Downloads");

        group.MapGet("/mapset/{id:int}/{beatmapId:int}", HandleDownloadBeatmap)
            .WithGroupName("basilapi")
            .WithSummary("Download one difficulty's .osu file, by mapset id and beatmap id.")
            .WithDescription("Serves the raw `.osu` difficulty file. 404 if the beatmap doesn't exist, " +
                "doesn't belong to this mapset, or its file is missing on disk. Content-Type " +
                "`application/x-osu-beatmap`. Public, no admin key.")
            .WithTags("Beatmap Downloads");

        group.MapGet("/mapset/{id:int}/sb", HandleDownloadStoryboard)
            .WithGroupName("basilapi")
            .WithSummary("Download a mapset's storyboard file, by mapset id.")
            .WithDescription("Serves the mapset folder's `.osb` storyboard file. A mapset is expected to " +
                "carry at most one; if more than one is somehow present, the first in filename order is " +
                "served. 404 if the mapset has no local folder, or the folder has no `.osb` file at all. " +
                "Content-Type `application/x-osu-storyboard`. Public, no admin key.")
            .WithTags("Beatmap Downloads");
    }

    private const string AdminKeyNote = RouteDocs.AdminKeyNote;

    private sealed record MapsetSummary(int Id, string Artist, string Title, string Creator, DateTime CreatedAt,
        DateTime LastUpdate, bool IsFrozen);

    private sealed record BeatmapBrief(int Id, string Version, GameMode Mode);

    private sealed record MapsetDetail(int Id, string Artist, string Title, string Creator, DateTime CreatedAt,
        DateTime LastUpdate, bool IsFrozen, IReadOnlyList<BeatmapBrief> Beatmaps);

    public sealed record FreezeBody(bool Frozen);

    private static async Task<IResult> HandleList([FromQuery] int? page, [FromQuery] int? pageSize,
        HttpContext context, IMapsetRepository mapsets, CancellationToken cancellationToken)
    {
        var (p, ps) = Pagination.Normalize(page, pageSize);
        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);

        var overqueried = await mapsets.FetchPageAsync((p - 1) * ps, ps + 1, !isAdmin, cancellationToken);
        var items = overqueried
            .Select(m => new MapsetSummary(m.Id, m.Artist, m.Title, m.Creator, m.CreatedAt, m.LastUpdate, m.IsFrozen))
            .ToList();

        return Results.Json(Pagination.Trim(items, p, ps));
    }

    private static async Task<IResult> HandleCreate(HttpContext context, IOptions<StorageOptions> storage,
        BeatmapIngestionService ingestion, CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType) return Results.BadRequest("Expected a multipart file upload.");

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return Results.BadRequest("Missing 'file' form field.");

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".osz", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("Only .osz uploads are accepted — a single .osu file has no set context.");

        Directory.CreateDirectory(storage.Value.MapsetsPath);
        var destinationName = $"{Guid.NewGuid():N}{extension}";
        var destination = Path.Combine(storage.Value.MapsetsPath, Path.GetFileName(destinationName));
        await using (var fileStream = File.Create(destination))
        {
            await file.CopyToAsync(fileStream, cancellationToken);
        }

        var ingested = await ingestion.ReconcileAllAsync(cancellationToken);
        return Results.Json(new { ingested });
    }

    private static async Task<IResult> HandleGet(int id, HttpContext context, IMapsetRepository mapsets,
        IMapRepository maps, CancellationToken cancellationToken)
    {
        var mapset = await mapsets.FetchByIdAsync(id, cancellationToken);
        if (mapset is null) return Results.NotFound();

        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
        var beatmaps = await maps.FetchAllBySetIdAsync(id, isAdmin, cancellationToken);
        if (beatmaps.Count == 0 && !isAdmin) return Results.NotFound();

        return Results.Json(new MapsetDetail(mapset.Id, mapset.Artist, mapset.Title, mapset.Creator,
            mapset.CreatedAt, mapset.LastUpdate, mapset.IsFrozen,
            beatmaps.Select(b => new BeatmapBrief(b.Id, b.Version, b.Difficulty.Mode)).ToList()));
    }

    private static async Task<IResult> HandleReplace(int id, HttpContext context, IMapsetRepository mapsets,
        IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var mapset = await mapsets.FetchByIdAsync(id, cancellationToken);
        if (mapset is null) return Results.NotFound();
        if (mapset.IsFrozen) return Results.Conflict(new { error = "This mapset is frozen and cannot be modified." });

        if (!context.Request.HasFormContentType) return Results.BadRequest("Expected a multipart file upload.");
        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return Results.BadRequest("Missing 'file' form field.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".osz", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("Only .osz uploads are accepted.");

        var targetFolder = BeatmapIngestionService.FindMapsetFolder(storage.Value, id);
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

    private static async Task<IResult> HandleDelete(int id, IMapsetRepository mapsets,
        IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var mapset = await mapsets.FetchByIdAsync(id, cancellationToken);
        if (mapset is null) return Results.NotFound();
        if (mapset.IsFrozen) return Results.Conflict(new { error = "This mapset is frozen and cannot be deleted." });

        var folder = BeatmapIngestionService.FindMapsetFolder(storage.Value, id);
        if (folder is null) return Results.NotFound();

        var deletedFolder = folder + BeatmapIngestionService.DeletedFolderInfix + Guid.NewGuid().ToString("N");
        try
        {
            Directory.Move(folder, deletedFolder);
        }
        catch (IOException)
        {
            return Results.Conflict(new { error = "The mapset's files are currently in use; try again shortly." });
        }

        return Results.Accepted();
    }

    private static async Task<IResult> HandleFreeze(int id, FreezeBody body, IMapsetRepository mapsets,
        CancellationToken cancellationToken)
    {
        if (await mapsets.FetchByIdAsync(id, cancellationToken) is null) return Results.NotFound();

        await mapsets.SetFrozenAsync(id, body.Frozen, cancellationToken);
        return Results.Json(new { id, isFrozen = body.Frozen });
    }

    private static async Task<IResult> HandleDownloadBeatmap(int id, int beatmapId, IMapRepository maps,
        IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var bmap = await maps.FetchOneAsync(beatmapId, setId: id, includePrivate: true, cancellationToken: cancellationToken);
        if (bmap is null || bmap.Mapset.Id != id) return Results.NotFound();

        var osuPath = BeatmapIngestionService.OsuFilePath(storage.Value, bmap);
        return File.Exists(osuPath) ? Results.File(osuPath, "application/x-osu-beatmap") : Results.NotFound();
    }

    private static IResult HandleDownloadStoryboard(int id, IOptions<StorageOptions> storage)
    {
        var folder = BeatmapIngestionService.FindMapsetFolder(storage.Value, id);
        if (folder is null) return Results.NotFound();

        var osbPath = Directory.EnumerateFiles(folder, "*.osb").Order().FirstOrDefault();
        return osbPath is null ? Results.NotFound() : Results.File(osbPath, "application/x-osu-storyboard");
    }
}
