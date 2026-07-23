using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;
using Basil.Infrastructure.Beatmaps;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing;

/// <summary>
///     Admin-key-gated CRUD for beatmaps/users/replays/matches/seasonals —
///     bancho.py has no equivalent admin surface. Every route here sits behind
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
        MapUsers(admin);
        MapReplays(admin);
        MapMatches(admin);
        MapSeasonals(admin);
    }

    private static void MapBeatmaps(RouteGroupBuilder admin)
    {
        admin.MapGet("/beatmaps", async (
            [FromQuery] string? query, [FromQuery] int? mode,
            [FromQuery] int offset, [FromQuery] int amount,
            IMapRepository maps, CancellationToken cancellationToken) =>
        {
            var sets = await maps.SearchAsync(query, (GameMode?)mode, offset,
                amount == 0 ? 50 : amount, cancellationToken);
            return Results.Json(sets);
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: search/list beatmap sets.")
            .WithDescription("Returns beatmap sets matching `query`/`mode`, paged by `offset`/`amount` " +
                "(default page size 50). Response is an array of sets, each an array of that set's beatmaps." +
                AdminKeyNote)
            .WithTags("Admin: Beatmaps");

        // Accepts a single .osz upload (field name "file"), drops it into MapsetsPath and runs a
        // full reconciliation pass. A lone .osu has no set context under the folder-per-mapset
        // model — only a full archive is accepted here.
        admin.MapPost("/beatmaps", async (HttpContext context, IOptions<StorageOptions> storage,
            BeatmapIngestionService ingestion, CancellationToken cancellationToken) =>
        {
            if (!context.Request.HasFormContentType) return Results.BadRequest("Expected a multipart file upload.");

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest("Missing 'file' form field.");

            var extension = Path.GetExtension(file.FileName);
            if (!string.Equals(extension, ".osz", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Only .osz uploads are accepted — a single .osu file has no set context.");

            Directory.CreateDirectory(storage.Value.MapsetsPath);
            // Path.GetFileName strips any directory component a malicious filename could smuggle in.
            var destinationName = $"{Guid.NewGuid():N}{extension}";
            var destination = Path.Combine(storage.Value.MapsetsPath, Path.GetFileName(destinationName));
            await using (var fileStream = File.Create(destination))
            {
                await file.CopyToAsync(fileStream, cancellationToken);
            }

            var ingested = await ingestion.ReconcileAllAsync(cancellationToken);
            return Results.Json(new { ingested });
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: upload a beatmap set (.osz).")
            .WithDescription("Multipart upload, field name `file`, must be a `.osz` archive — a lone `.osu` file " +
                "has no set context under this server's folder-per-mapset storage model. After saving the " +
                "archive, runs a full ingestion reconciliation pass and returns `{ ingested }` (the number of " +
                "beatmaps added/updated)." + AdminKeyNote)
            .WithTags("Admin: Beatmaps");

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

        admin.MapDelete("/beatmaps/{id:int}", async (int id, IMapRepository maps, IOptions<StorageOptions> storage,
            CancellationToken cancellationToken) =>
        {
            var bmap = await maps.FetchOneAsync(id, includePrivate: true, cancellationToken: cancellationToken);
            if (bmap is null) return Results.NotFound();

            await maps.DeleteByMd5Async(bmap.Md5, cancellationToken);
            var osuPath = BeatmapIngestionService.OsuFilePath(storage.Value, bmap);
            if (File.Exists(osuPath)) File.Delete(osuPath);

            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: delete one beatmap, by beatmap id.")
            .WithDescription("Removes the beatmap's database row and, if present, its `.osu` file on disk. " +
                "204 on success, 404 if no beatmap with this id exists (frozen beatmaps included)." + AdminKeyNote)
            .WithTags("Admin: Beatmaps");
    }

    private static void MapUsers(RouteGroupBuilder admin)
    {
        admin.MapGet("/users", async (IUserRepository users, CancellationToken cancellationToken) =>
            Results.Json(await users.FetchAllAsync(cancellationToken)))
            .WithGroupName("basilapi")
            .WithSummary("Admin: list every user.")
            .WithDescription("Returns every user row as a JSON array, unfiltered and unpaged." + AdminKeyNote)
            .WithTags("Admin: Users");

        admin.MapGet("/users/{id:int}", async (int id, IUserRepository users, CancellationToken cancellationToken) =>
        {
            var user = await users.FetchByIdAsync(id, cancellationToken);
            return user is null ? Results.NotFound() : Results.Json(user);
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: get one user, by user id.")
            .WithDescription("Returns the user row as JSON. 404 if no user with this id exists." + AdminKeyNote)
            .WithTags("Admin: Users");

        admin.MapPost("/users", async (CreateUserRequest body, IUserRepository users,
            IPasswordHasher passwordHasher, CancellationToken cancellationToken) =>
        {
            if (!User.ValidateUsername(body.Name, out var usernameError))
                return Results.BadRequest(new { error = usernameError });

            if (await users.FetchByNameAsync(body.Name, cancellationToken) is not null)
                return Results.Conflict(new { error = "Username already exists." });

            var passwordMd5 = Convert.ToHexStringLower(MD5.HashData(
                Encoding.UTF8.GetBytes(body.Password)));
            var pwBcrypt = passwordHasher.Hash(Encoding.UTF8.GetBytes(passwordMd5));
            var user = await users.CreateAsync(body.Name, pwBcrypt, body.Country ?? "xx",
                (UserPrivileges?)body.Priv, cancellationToken);
            return user is null
                ? Results.Conflict(new { error = "Username already exists." })
                : Results.Json(user);
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: create a user directly (bypassing in-game registration).")
            .WithDescription("Body: `{ name, password, country?, priv? }` (`country` defaults to `\"xx\"`, " +
                "`priv` to the server's default privileges if omitted). The plaintext `password` is MD5'd then " +
                "bcrypt-hashed server-side, matching the real client's own hashing convention. 400 on an " +
                "invalid username, 409 if the name is already taken." + AdminKeyNote)
            .WithTags("Admin: Users");

        // Deliberately scoped to what IUserRepository already exposes (name/country/priv) rather
        // than a full field-by-field editor — see IUserRepository's per-field Update* methods.
        admin.MapPut("/users/{id:int}", async (int id, UpdateUserRequest body, IUserRepository users,
            CancellationToken cancellationToken) =>
        {
            if (await users.FetchByIdAsync(id, cancellationToken) is null) return Results.NotFound();

            if (body.Name is not null)
            {
                if (!User.ValidateUsername(body.Name, out var usernameError))
                    return Results.BadRequest(new { error = usernameError });

                await users.UpdateNameAsync(id, body.Name, User.MakeSafeName(body.Name), cancellationToken);
            }
            if (body.Country is not null)
                await users.UpdateCountryAsync(id, body.Country, cancellationToken);
            if (body.Priv is not null)
                await users.UpdatePrivilegesAsync(id, (UserPrivileges)body.Priv.Value, cancellationToken);

            return Results.Json(await users.FetchByIdAsync(id, cancellationToken));
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: partially update a user (name/country/privileges).")
            .WithDescription("Body: `{ name?, country?, priv? }` — each field is updated only if present; " +
                "omitted fields are left unchanged. Deliberately limited to these three fields (no full " +
                "field-by-field editor exists). Returns the updated user row. 404 if no user with this id " +
                "exists; 400 on an invalid new username." + AdminKeyNote)
            .WithTags("Admin: Users");

        admin.MapPost("/users/{id:int}/avatar", async (int id, HttpContext context, IOptions<StorageOptions> storage,
            CancellationToken cancellationToken) =>
        {
            if (!context.Request.HasFormContentType) return Results.BadRequest("Expected a multipart file upload.");

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest("Missing 'file' form field.");

            var extension = Path.GetExtension(file.FileName);
            Directory.CreateDirectory(storage.Value.AvatarsPath);
            foreach (var existing in Directory.EnumerateFiles(storage.Value.AvatarsPath, $"{id}.*"))
                File.Delete(existing);

            var destination = Path.Combine(storage.Value.AvatarsPath, $"{id}{extension}");
            await using var fileStream = File.Create(destination);
            await file.CopyToAsync(fileStream, cancellationToken);

            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: upload a user's avatar image.")
            .WithDescription("Multipart upload, field name `file`. Replaces any existing avatar for this user id " +
                "(any prior file with a different extension is deleted first). 204 on success." + AdminKeyNote)
            .WithTags("Admin: Users");

        // Soft delete: zeroes privileges rather than removing the row, so score/social/anticheat
        // history referencing this user's id stays intact — matches how restriction/ban already
        // works in this server (a privilege bit, never a hard delete).
        admin.MapDelete("/users/{id:int}", async (int id, IUserRepository users, CancellationToken cancellationToken) =>
        {
            if (await users.FetchByIdAsync(id, cancellationToken) is null) return Results.NotFound();

            await users.UpdatePrivilegesAsync(id, 0, cancellationToken);
            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: soft-delete a user.")
            .WithDescription("Zeroes the user's privilege bits rather than removing the row, so score/social/" +
                "anticheat history referencing this user id stays intact — the same convention this server " +
                "already uses for restriction/ban. 204 on success, 404 if no user with this id exists." +
                AdminKeyNote)
            .WithTags("Admin: Users");

        // Blocking a specific user (as opposed to ToggleBlockNonFriendDms's blanket "block everyone
        // who isn't a friend" toggle) has no client packet to trigger it — osu! stable only ever
        // sends FriendAdd/FriendRemove over the wire. Admin-key-gated CRUD is the only surface for it.
        admin.MapPost("/users/{id:int}/block/{targetId:int}", async (int id, int targetId,
            IRelationshipRepository relationships, CancellationToken cancellationToken) =>
        {
            if (await relationships.FetchOneAsync(id, targetId, cancellationToken) is null)
                await relationships.CreateAsync(id, targetId, RelationshipType.Block, cancellationToken);
            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: block one user from another (one-directional).")
            .WithDescription("Creates a Block relationship from `{id}` toward `{targetId}` if one doesn't " +
                "already exist. The real osu! client has no packet for blocking a specific user (only bulk " +
                "FriendAdd/FriendRemove) — this admin route is the only way to set one up. 204 on success " +
                "(idempotent — already-blocked is not an error)." + AdminKeyNote)
            .WithTags("Admin: Users");

        admin.MapDelete("/users/{id:int}/block/{targetId:int}", async (int id, int targetId,
            IRelationshipRepository relationships, CancellationToken cancellationToken) =>
        {
            var relationship = await relationships.FetchOneAsync(id, targetId, cancellationToken);
            if (relationship?.Type == RelationshipType.Block)
                await relationships.DeleteAsync(id, targetId, cancellationToken);
            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: remove a block relationship between two users.")
            .WithDescription("Removes the Block relationship from `{id}` toward `{targetId}`, if one exists. " +
                "204 on success (idempotent — no existing block is not an error)." + AdminKeyNote)
            .WithTags("Admin: Users");
    }

    private static void MapReplays(RouteGroupBuilder admin)
    {
        admin.MapGet("/replays", (IOptions<StorageOptions> storage) =>
        {
            Directory.CreateDirectory(storage.Value.ReplaysPath);
            var scoreIds = Directory.EnumerateFiles(storage.Value.ReplaysPath, "*.osr")
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Where(name => long.TryParse(name, out _))
                .Select(long.Parse)
                .ToArray();
            return Results.Json(scoreIds);
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: list every score id that has a stored replay file.")
            .WithDescription("Derived from the replay storage folder's filenames (`{scoreId}.osr`), not the " +
                "database — a score without an uploaded replay simply won't appear here." + AdminKeyNote)
            .WithTags("Admin: Replays");

        admin.MapDelete("/replays/{scoreId:long}", (long scoreId, IOptions<StorageOptions> storage) =>
        {
            var path = Path.Combine(storage.Value.ReplaysPath, $"{scoreId}.osr");
            if (!File.Exists(path)) return Results.NotFound();

            File.Delete(path);
            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithSummary("Admin: delete one replay file, by score id.")
            .WithDescription("Deletes the `.osr` file only — the score row itself is untouched. 204 on success, " +
                "404 if no replay file exists for this score id." + AdminKeyNote)
            .WithTags("Admin: Replays");
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

public sealed record CreateUserRequest(string Name, string Password, string? Country, int? Priv);

public sealed record UpdateUserRequest(string? Name, string? Country, int? Priv);
