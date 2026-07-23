using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Configuration;
using Basil.Domain.Beatmaps;
using Basil.Infrastructure.Beatmaps;
using Basil.Web.Auth;
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
            .WithSummary("List beatmapsets, paged.")
            .WithDescription("Query params: `page` (default 1), `pageSize` (default 50). A private mapset " +
                "is excluded entirely unless the caller carries a valid `X-Admin-Key`. Response: " +
                "`{ page, pageSize, count, hasMore, items }`. Public.")
            .WithTags("Beatmapsets");

        group.MapPost("/beatmapsets", HandleCreate)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Upload a beatmap set (.osz).")
            .WithDescription("Multipart upload, field name `file`, must be a `.osz` archive — a lone `.osu` " +
                "file has no set context under this server's folder-per-mapset storage model. Runs a full " +
                "ingestion reconciliation pass synchronously and returns `{ ingested }` (the number of " +
                "beatmaps added/updated)." + AdminKeyNote)
            .WithTags("Beatmapsets");

        group.MapGet("/beatmapsets/{beatmapsetId:int}", HandleGet)
            .WithGroupName("basilapi")
            .WithSummary("Get one beatmapset's info, by beatmapset id.")
            .WithDescription("Returns `{ id, artist, title, creator, createdAt, lastUpdate, isFrozen, " +
                "isPrivate, beatmaps: [{ id, version, mode }] }` — beatmap ids are included inline so a " +
                "client doesn't need a second call to discover them. 404 if the mapset doesn't exist, or " +
                "(for a non-admin caller) it's private. Public, with a soft admin elevation.")
            .WithTags("Beatmapsets");

        group.MapPut("/beatmapsets/{beatmapsetId:int}", HandleReplace)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Replace a beatmapset's archive (re-ingest), asynchronously.")
            .WithDescription("Multipart upload, field name `file`, must be a `.osz` archive. Filesystem-only " +
                "and asynchronous: extracts the new archive's contents directly into the mapset's existing " +
                "storage folder (overwriting files), then returns `202 Accepted` immediately — the database " +
                "catches up shortly after via the same live reconciliation the filesystem watcher already " +
                "runs, not synchronously in this request. 404 if the mapset doesn't exist; 409 if it's " +
                "frozen (see `PATCH /beatmapsets/{beatmapsetId}`)." + AdminKeyNote)
            .WithTags("Beatmapsets");

        group.MapDelete("/beatmapsets/{beatmapsetId:int}", HandleDelete)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Delete a beatmapset, asynchronously.")
            .WithDescription("Filesystem-only and asynchronous: atomically renames the mapset's storage " +
                "folder in place (a TOCTOU-safe marker the live reconciliation and a background garbage " +
                "collector both recognize as \"gone\"), then returns `202 Accepted` — the database row and " +
                "the physical folder are both cleaned up shortly after, not synchronously in this request. " +
                "404 if the mapset doesn't exist; 409 (folder left untouched) if the rename itself fails " +
                "(e.g. a locked file) or if the mapset is frozen (see `PATCH /beatmapsets/{beatmapsetId}`)." +
                AdminKeyNote)
            .WithTags("Beatmapsets");

        group.MapPatch("/beatmapsets/{beatmapsetId:int}", HandlePatch)
            .RequireAuthorization(AdminKeyDefaults.Policy)
            .WithGroupName("basilapi")
            .WithSummary("Change a beatmapset's freeze and/or private flags.")
            .WithDescription("Body: `{ frozen?, private? }` — each field is applied only if present. " +
                "`frozen` is a write-lock: while set, `PUT`/`DELETE /beatmapsets/{beatmapsetId}` are " +
                "rejected with 409 regardless of admin role (this route itself is exempt, so unfreezing is " +
                "always possible). `private` hides the mapset (and every beatmap under it) from non-admin " +
                "listings/lookups. Returns the updated beatmapset info (same shape as `GET`). 404 if the " +
                "mapset doesn't exist." + AdminKeyNote)
            .WithTags("Beatmapsets");

        group.MapGet("/beatmapsets/{beatmapsetId:int}/{beatmapId:int}", HandleBeatmapInfo)
            .WithGroupName("basilapi")
            .WithSummary("Get one difficulty's metadata, by beatmapset id and beatmap id.")
            .WithDescription("Returns `{ id, version, mode, filename, totalLength, maxCombo, plays, " +
                "passes }`. 404 if the beatmap doesn't exist, doesn't belong to this mapset, or the parent " +
                "mapset is private and the caller isn't admin. Public, with a soft admin elevation.")
            .WithTags("Beatmapsets");

        group.MapGet("/beatmapsets/{beatmapsetId:int}/{beatmapId:int}/download", HandleDownloadBeatmap)
            .WithGroupName("basilapi")
            .WithSummary("Download one difficulty's .osu file, by beatmapset id and beatmap id.")
            .WithDescription("Serves the raw `.osu` difficulty file. 404 if the beatmap doesn't exist, " +
                "doesn't belong to this mapset, its file is missing on disk, or the parent mapset is " +
                "private and the caller isn't admin. Content-Type `application/x-osu-beatmap`. Public, " +
                "with a soft admin elevation.")
            .WithTags("Beatmapsets");

        group.MapGet("/beatmapsets/{beatmapsetId:int}/storyboard", HandleDownloadStoryboard)
            .WithGroupName("basilapi")
            .WithSummary("Download a beatmapset's storyboard file, by beatmapset id.")
            .WithDescription("Serves the mapset folder's `.osb` storyboard file. A mapset is expected to " +
                "carry at most one; if more than one is somehow present, the first in filename order is " +
                "served. 404 if the mapset has no local folder, or the folder has no `.osb` file at all. " +
                "Content-Type `application/x-osu-storyboard`. Public, no admin key.")
            .WithTags("Beatmapsets");

        group.MapGet("/beatmapsets/{beatmapsetId:int}/download", HandleDownloadArchive)
            .WithGroupName("basilapi")
            .WithSummary("Download a beatmapset as a .osz archive, by beatmapset id.")
            .WithDescription("Builds a fresh `.osz` on the fly from the mapset's local storage folder (every " +
                "file in the folder — audio, images, video, every `.osu`/`.osb`) and serves it. 404 if the " +
                "mapset has no local folder, or the folder is empty. Content-Type " +
                "`application/x-osu-beatmap-archive`. Public, no admin key.")
            .WithTags("Beatmapsets");
    }

    private const string AdminKeyNote = RouteDocs.AdminKeyNote;

    private sealed record BeatmapsetSummary(int Id, string Artist, string Title, string Creator, DateTime CreatedAt,
        DateTime LastUpdate, bool IsFrozen, bool IsPrivate);

    private sealed record BeatmapBrief(int Id, string Version, GameMode Mode);

    private sealed record BeatmapsetDetail(int Id, string Artist, string Title, string Creator, DateTime CreatedAt,
        DateTime LastUpdate, bool IsFrozen, bool IsPrivate, IReadOnlyList<BeatmapBrief> Beatmaps);

    private sealed record BeatmapDetail(int Id, string Version, GameMode Mode, string Filename,
        TimeSpan TotalLength, int MaxCombo, int Plays, int Passes);

    public sealed record BeatmapsetPatchBody(bool? Frozen, bool? Private);

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

    private static async Task<IResult> HandleGet(int beatmapsetId, HttpContext context, IMapsetRepository mapsets,
        IMapRepository maps, CancellationToken cancellationToken)
    {
        var mapset = await mapsets.FetchByIdAsync(beatmapsetId, cancellationToken);
        if (mapset is null) return Results.NotFound();

        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
        if (mapset.IsPrivate && !isAdmin) return Results.NotFound();

        var beatmaps = await maps.FetchAllBySetIdAsync(beatmapsetId, isAdmin, cancellationToken);
        return Results.Json(BuildDetail(mapset, beatmaps));
    }

    private static BeatmapsetDetail BuildDetail(Mapset mapset, IReadOnlyList<Beatmap> beatmaps)
    {
        return new BeatmapsetDetail(mapset.Id, mapset.Artist, mapset.Title, mapset.Creator, mapset.CreatedAt,
            mapset.LastUpdate, mapset.IsFrozen, mapset.IsPrivate,
            beatmaps.Select(b => new BeatmapBrief(b.Id, b.Version, b.Difficulty.Mode)).ToList());
    }

    private static async Task<IResult> HandleReplace(int beatmapsetId, HttpContext context, IMapsetRepository mapsets,
        IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var mapset = await mapsets.FetchByIdAsync(beatmapsetId, cancellationToken);
        if (mapset is null) return Results.NotFound();
        if (mapset.IsFrozen) return Results.Conflict(new { error = "This mapset is frozen and cannot be modified." });

        if (!context.Request.HasFormContentType) return Results.BadRequest("Expected a multipart file upload.");
        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null) return Results.BadRequest("Missing 'file' form field.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".osz", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("Only .osz uploads are accepted.");

        var targetFolder = BeatmapIngestionService.FindMapsetFolder(storage.Value, beatmapsetId);
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

    private static async Task<IResult> HandleDelete(int beatmapsetId, IMapsetRepository mapsets,
        IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var mapset = await mapsets.FetchByIdAsync(beatmapsetId, cancellationToken);
        if (mapset is null) return Results.NotFound();
        if (mapset.IsFrozen) return Results.Conflict(new { error = "This mapset is frozen and cannot be deleted." });

        var folder = BeatmapIngestionService.FindMapsetFolder(storage.Value, beatmapsetId);
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

    private static async Task<IResult> HandlePatch(int beatmapsetId, BeatmapsetPatchBody body,
        IMapsetRepository mapsets, IMapRepository maps, CancellationToken cancellationToken)
    {
        if (await mapsets.FetchByIdAsync(beatmapsetId, cancellationToken) is null) return Results.NotFound();

        if (body.Frozen is not null) await mapsets.SetFrozenAsync(beatmapsetId, body.Frozen.Value, cancellationToken);
        if (body.Private is not null) await mapsets.SetPrivateAsync(beatmapsetId, body.Private.Value, cancellationToken);

        var updated = await mapsets.FetchByIdAsync(beatmapsetId, cancellationToken);
        var beatmaps = await maps.FetchAllBySetIdAsync(beatmapsetId, includePrivate: true,
            cancellationToken: cancellationToken);
        return Results.Json(BuildDetail(updated!, beatmaps));
    }

    private static async Task<IResult> HandleBeatmapInfo(int beatmapsetId, int beatmapId, HttpContext context,
        IMapRepository maps, CancellationToken cancellationToken)
    {
        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
        var bmap = await maps.FetchOneAsync(beatmapId, setId: beatmapsetId, includePrivate: isAdmin,
            cancellationToken: cancellationToken);
        if (bmap is null || bmap.Mapset.Id != beatmapsetId) return Results.NotFound();

        return Results.Json(new BeatmapDetail(bmap.Id, bmap.Version, bmap.Difficulty.Mode, bmap.Filename,
            bmap.TotalLength, bmap.MaxCombo, bmap.Plays, bmap.Passes));
    }

    private static async Task<IResult> HandleDownloadBeatmap(int beatmapsetId, int beatmapId, HttpContext context,
        IMapRepository maps, IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var isAdmin = context.User.IsInRole(AdminKeyDefaults.Role);
        var bmap = await maps.FetchOneAsync(beatmapId, setId: beatmapsetId, includePrivate: isAdmin,
            cancellationToken: cancellationToken);
        if (bmap is null || bmap.Mapset.Id != beatmapsetId) return Results.NotFound();

        var osuPath = BeatmapIngestionService.OsuFilePath(storage.Value, bmap);
        return File.Exists(osuPath) ? Results.File(osuPath, "application/x-osu-beatmap") : Results.NotFound();
    }

    private static IResult HandleDownloadStoryboard(int beatmapsetId, IOptions<StorageOptions> storage)
    {
        var folder = BeatmapIngestionService.FindMapsetFolder(storage.Value, beatmapsetId);
        if (folder is null) return Results.NotFound();

        var osbPath = Directory.EnumerateFiles(folder, "*.osb").Order().FirstOrDefault();
        return osbPath is null ? Results.NotFound() : Results.File(osbPath, "application/x-osu-storyboard");
    }

    private static async Task<IResult> HandleDownloadArchive(int beatmapsetId, IMapRepository maps,
        IOptions<StorageOptions> storage, CancellationToken cancellationToken)
    {
        var osz = await BanchoHostGroups.BuildOszArchiveAsync(maps, storage.Value, beatmapsetId, false, cancellationToken);
        return osz is null
            ? Results.NotFound()
            : Results.File(osz.Value.Bytes, "application/x-osu-beatmap-archive", osz.Value.FileName);
    }
}
