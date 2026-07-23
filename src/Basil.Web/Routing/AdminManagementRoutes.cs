using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Configuration;
using Basil.Infrastructure.Beatmaps;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing;

/// <summary>
///     What's left of the old admin-key-gated CRUD block after most of it was folded into the
///     resource-oriented `/mapset`, `/user`, `/match`, and `/score` route files: beatmap rescan,
///     match deletion, and seasonals (soon to move to `/seasonal` too). Every route here sits behind
///     <see cref="AdminKeyFilter" />.
/// </summary>
internal static class AdminManagementRoutes
{
    private const string AdminKeyNote = " Requires a valid `X-Admin-Key` request header matching the " +
        "server's configured `Server:AdminKey` — 401 if missing, wrong, or if `Server:AdminKey` is unset " +
        "(the whole management surface is locked with no fallback-open mode).";

    public static void MapAdminManagement(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("").AddEndpointFilter<AdminKeyFilter>();

        MapBeatmaps(admin);
        MapMatches(admin);
        MapSeasonals(admin);
    }

    // Search/list, upload, and delete are superseded by the public GET/POST /mapset routes (see
    // MapsetRoutes.cs) — this redesign has no beatmap-level delete, only mapset-level. Only the
    // rescan trigger has no public equivalent, so it stays here.
    private static void MapBeatmaps(RouteGroupBuilder admin)
    {
        admin.MapPost("/beatmaps/rescan",
            async (BeatmapIngestionService ingestion, CancellationToken cancellationToken) =>
            {
                var ingested = await ingestion.ReconcileAllAsync(cancellationToken);
                return Results.Json(new { ingested });
            })
            .WithGroupName("basilapi")
            .WithSummary("Admin: force a full beatmap storage reconciliation pass.")
            .WithDescription("Re-scans the entire mapsets storage folder against the database (extracting any " +
                "loose `.osz` archives found at the root, ingesting new/changed mapsets, and deleting rows for " +
                "mapsets whose folder no longer exists). Returns `{ ingested }`. Runs automatically at server " +
                "startup too — this endpoint is for triggering it on demand (e.g. after manually copying files " +
                "into the storage folder)." + AdminKeyNote)
            .WithTags("Admin: Beatmaps");
    }

    // List/create/settings/actions moved to the public-facing MatchRoutes.MapMatchRoutes (GET/POST
    // /match, GET+PUT+PATCH /match/{id}/settings, POST /match/{id}/{action}) — this admin-prefixed
    // surface now only keeps historical-record deletion, which has no public equivalent.
    private static void MapMatches(RouteGroupBuilder admin)
    {
        admin.MapDelete("/matches/{id:int}", async (int id, IMatchPersistenceRepository matchPersistence,
            CancellationToken cancellationToken) =>
        {
            if (await matchPersistence.FetchMatchAsync(id, cancellationToken) is null) return Results.NotFound();

            await matchPersistence.DeleteMatchAsync(id, cancellationToken);
            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: delete a match and everything under it.")
            .WithDescription("Cascading delete: the match row plus every round and score linked to it. Does not " +
                "affect a match still in progress at the protocol level — this only removes persisted history. " +
                "204 on success, 404 if no match with this id exists." + AdminKeyNote)
            .WithTags("Admin: Matches");
    }

    private static void MapSeasonals(RouteGroupBuilder admin)
    {
        admin.MapGet("/seasonals", (IOptions<StorageOptions> storage) =>
        {
            Directory.CreateDirectory(storage.Value.SeasonalsPath);
            var files = Directory.EnumerateFiles(storage.Value.SeasonalsPath).Select(Path.GetFileName).ToArray();
            return Results.Json(files);
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: list seasonal background image filenames.")
            .WithDescription("Returns bare filenames (unlike the osu! client-facing " +
                "`GET osu.<domain>/web/osu-getseasonal.php`, which returns full URLs for the same folder)." +
                AdminKeyNote)
            .WithTags("Admin: Seasonals");

        admin.MapPost("/seasonals", async (HttpContext context, IOptions<StorageOptions> storage,
            CancellationToken cancellationToken) =>
        {
            if (!context.Request.HasFormContentType) return Results.BadRequest("Expected a multipart file upload.");

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest("Missing 'file' form field.");

            Directory.CreateDirectory(storage.Value.SeasonalsPath);
            // Path.GetFileName strips any directory component a malicious filename could smuggle in.
            var destination = Path.Combine(storage.Value.SeasonalsPath, Path.GetFileName(file.FileName));
            await using var fileStream = File.Create(destination);
            await file.CopyToAsync(fileStream, cancellationToken);

            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: upload a seasonal background image.")
            .WithDescription("Multipart upload, field name `file`. Saved under its own uploaded filename " +
                "(path-traversal-filtered). 204 on success." + AdminKeyNote)
            .WithTags("Admin: Seasonals");

        admin.MapDelete("/seasonals/{fileName}", (string fileName, IOptions<StorageOptions> storage) =>
        {
            var path = Path.Combine(storage.Value.SeasonalsPath, Path.GetFileName(fileName));
            if (!File.Exists(path)) return Results.NotFound();

            File.Delete(path);
            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: delete one seasonal background image, by filename.")
            .WithDescription("204 on success, 404 if the file doesn't exist." + AdminKeyNote)
            .WithTags("Admin: Seasonals");
    }
}
