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
        });

        admin.MapGet("/beatmaps/{id:int}", async (int id, IMapRepository maps, CancellationToken cancellationToken) =>
        {
            var bmap = await maps.FetchOneAsync(id, includeFrozen: true, cancellationToken: cancellationToken);
            return bmap is null ? Results.NotFound() : Results.Json(bmap);
        });

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
        });

        admin.MapPost("/beatmaps/rescan",
            async (BeatmapIngestionService ingestion, CancellationToken cancellationToken) =>
            {
                var ingested = await ingestion.ReconcileAllAsync(cancellationToken);
                return Results.Json(new { ingested });
            });

        admin.MapDelete("/beatmaps/{id:int}", async (int id, IMapRepository maps, IOptions<StorageOptions> storage,
            CancellationToken cancellationToken) =>
        {
            var bmap = await maps.FetchOneAsync(id, includeFrozen: true, cancellationToken: cancellationToken);
            if (bmap is null) return Results.NotFound();

            await maps.DeleteByMd5Async(bmap.Md5, cancellationToken);
            var osuPath = BeatmapIngestionService.OsuFilePath(storage.Value, bmap);
            if (File.Exists(osuPath)) File.Delete(osuPath);

            return Results.NoContent();
        });
    }

    private static void MapUsers(RouteGroupBuilder admin)
    {
        admin.MapGet("/users", async (IUserRepository users, CancellationToken cancellationToken) =>
            Results.Json(await users.FetchAllAsync(cancellationToken)));

        admin.MapGet("/users/{id:int}", async (int id, IUserRepository users, CancellationToken cancellationToken) =>
        {
            var user = await users.FetchByIdAsync(id, cancellationToken);
            return user is null ? Results.NotFound() : Results.Json(user);
        });

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
        });

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
        });

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
        });

        // Soft delete: zeroes privileges rather than removing the row, so score/social/anticheat
        // history referencing this user's id stays intact — matches how restriction/ban already
        // works in this server (a privilege bit, never a hard delete).
        admin.MapDelete("/users/{id:int}", async (int id, IUserRepository users, CancellationToken cancellationToken) =>
        {
            if (await users.FetchByIdAsync(id, cancellationToken) is null) return Results.NotFound();

            await users.UpdatePrivilegesAsync(id, 0, cancellationToken);
            return Results.NoContent();
        });

        // Blocking a specific user (as opposed to ToggleBlockNonFriendDms's blanket "block everyone
        // who isn't a friend" toggle) has no client packet to trigger it — osu! stable only ever
        // sends FriendAdd/FriendRemove over the wire. Admin-key-gated CRUD is the only surface for it.
        admin.MapPost("/users/{id:int}/block/{targetId:int}", async (int id, int targetId,
            IRelationshipRepository relationships, CancellationToken cancellationToken) =>
        {
            if (await relationships.FetchOneAsync(id, targetId, cancellationToken) is null)
                await relationships.CreateAsync(id, targetId, RelationshipType.Block, cancellationToken);
            return Results.NoContent();
        });

        admin.MapDelete("/users/{id:int}/block/{targetId:int}", async (int id, int targetId,
            IRelationshipRepository relationships, CancellationToken cancellationToken) =>
        {
            var relationship = await relationships.FetchOneAsync(id, targetId, cancellationToken);
            if (relationship?.Type == RelationshipType.Block)
                await relationships.DeleteAsync(id, targetId, cancellationToken);
            return Results.NoContent();
        });
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
        });

        admin.MapDelete("/replays/{scoreId:long}", (long scoreId, IOptions<StorageOptions> storage) =>
        {
            var path = Path.Combine(storage.Value.ReplaysPath, $"{scoreId}.osr");
            if (!File.Exists(path)) return Results.NotFound();

            File.Delete(path);
            return Results.NoContent();
        });
    }

    private static void MapMatches(RouteGroupBuilder admin)
    {
        admin.MapGet("/matches", async (IMatchPersistenceRepository matchPersistence,
                CancellationToken cancellationToken) =>
            Results.Json(await matchPersistence.FetchAllMatchesAsync(cancellationToken)));

        admin.MapDelete("/matches/{id:int}", async (int id, IMatchPersistenceRepository matchPersistence,
            CancellationToken cancellationToken) =>
        {
            if (await matchPersistence.FetchMatchAsync(id, cancellationToken) is null) return Results.NotFound();

            await matchPersistence.DeleteMatchAsync(id, cancellationToken);
            return Results.NoContent();
        });

        admin.MapPut("/match/{id:int}/privacy", async (int id, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var body = await context.Request.ReadFromJsonAsync<PrivacyBody>(cancellationToken);
            if (body is null) return Results.BadRequest();

            var matchRegistry = context.RequestServices.GetRequiredService<IMatchRegistry>();
            var match = matchRegistry.GetByDbId(id);
            if (match is null) return Results.NotFound();

            await match.Lock.WaitAsync(cancellationToken);
            try
            {
                match.IsPrivate = body.IsPrivate;
            }
            finally
            {
                match.Lock.Release();
            }

            var membership = context.RequestServices.GetRequiredService<MatchMembershipService>();
            membership.EnqueueState(match);

            return Results.Json(new { isPrivate = match.IsPrivate });
        });
    }

    private static void MapSeasonals(RouteGroupBuilder admin)
    {
        admin.MapGet("/seasonals", (IOptions<StorageOptions> storage) =>
        {
            Directory.CreateDirectory(storage.Value.SeasonalsPath);
            var files = Directory.EnumerateFiles(storage.Value.SeasonalsPath).Select(Path.GetFileName).ToArray();
            return Results.Json(files);
        });

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
        });

        admin.MapDelete("/seasonals/{fileName}", (string fileName, IOptions<StorageOptions> storage) =>
        {
            var path = Path.Combine(storage.Value.SeasonalsPath, Path.GetFileName(fileName));
            if (!File.Exists(path)) return Results.NotFound();

            File.Delete(path);
            return Results.NoContent();
        });
    }
}

public sealed record CreateUserRequest(string Name, string Password, string? Country, int? Priv);

public sealed record UpdateUserRequest(string? Name, string? Country, int? Priv);

public sealed record PrivacyBody(bool IsPrivate);