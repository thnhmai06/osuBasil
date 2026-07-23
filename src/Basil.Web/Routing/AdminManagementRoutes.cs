using Basil.Application.Abstractions.Multiplayer;
using Basil.Infrastructure.Beatmaps;

namespace Basil.Web.Routing;

/// <summary>
///     What's left of the old admin-key-gated CRUD block after most of it was folded into the
///     resource-oriented `/mapset`, `/user`, `/match`, `/score`, `/faq`, and `/seasonal` route files:
///     just beatmap rescan and match deletion, neither of which has a public equivalent. Every route
///     here sits behind <see cref="AdminKeyFilter" />.
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
}
