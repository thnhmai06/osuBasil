using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions.Spectating;
using Basil.Domain.Users;
using Basil.Web.Auth;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing;

/// <summary>
///     `/user` — renamed from the old plural `/users` admin CRUD surface. Fully admin-key gated
///     (reads and writes) except <c>GET /user/{id}/live</c>, which stays public — a direct rename of
///     the old `/spec/{id}` route, now blocked for user id 0 (BasilBot has no gameplay stream of its
///     own to spectate). Block/unblock (`POST`/`DELETE /users/{id}/block/{targetId}`) is dropped
///     entirely — no replacement.
/// </summary>
internal static class UserRoutes
{
    private const string AdminKeyNote = " Requires a valid `X-Admin-Key` request header matching the " +
        "server's configured `Server:AdminKey`.";

    public static void MapUserRoutes(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/user").RequireAuthorization(AdminKeyDefaults.Policy);

        admin.MapGet("", async (IUserRepository users, CancellationToken cancellationToken) =>
            Results.Json(await users.FetchAllAsync(cancellationToken)))
            .WithGroupName("basilapi")
            .WithSummary("List every user.")
            .WithDescription("Returns every user row as a JSON array, unfiltered and unpaged." + AdminKeyNote)
            .WithTags("Users");

        admin.MapGet("/{id:int}", async (int id, IUserRepository users, CancellationToken cancellationToken) =>
        {
            var user = await users.FetchByIdAsync(id, cancellationToken);
            return user is null ? Results.NotFound() : Results.Json(user);
        })
            .WithGroupName("basilapi")
            .WithSummary("Get one user, by user id.")
            .WithDescription("Returns the user row as JSON. 404 if no user with this id exists." + AdminKeyNote)
            .WithTags("Users");

        admin.MapPost("", async (CreateUserRequest body, IUserRepository users,
            IPasswordHasher passwordHasher, CancellationToken cancellationToken) =>
        {
            if (!User.ValidateUsername(body.Name, out var usernameError))
                return Results.BadRequest(new { error = usernameError });

            if (await users.FetchByNameAsync(body.Name, cancellationToken) is not null)
                return Results.Conflict(new { error = "Username already exists." });

            var passwordMd5 = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(body.Password)));
            var pwBcrypt = passwordHasher.Hash(Encoding.UTF8.GetBytes(passwordMd5));
            var user = await users.CreateAsync(body.Name, pwBcrypt, body.Country ?? "xx",
                (UserPrivileges?)body.Priv, cancellationToken);
            return user is null
                ? Results.Conflict(new { error = "Username already exists." })
                : Results.Json(user);
        })
            .WithGroupName("basilapi")
            .WithSummary("Create a user directly (bypassing in-game registration).")
            .WithDescription("Body: `{ name, password, country?, priv? }` (`country` defaults to `\"xx\"`, " +
                "`priv` to the server's default privileges if omitted). The plaintext `password` is MD5'd then " +
                "bcrypt-hashed server-side, matching the real client's own hashing convention. 400 on an " +
                "invalid username, 409 if the name is already taken." + AdminKeyNote)
            .WithTags("Users");

        admin.MapMethods("/{id:int}", ["PUT", "PATCH"], async (int id, UpdateUserRequest body, IUserRepository users,
            CancellationToken cancellationToken) =>
        {
            if (id == BotBootstrapServiceBotId) return Results.BadRequest(new { error = "Cannot modify BasilBot." });
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
            .WithSummary("Partially update a user (name/country/privileges).")
            .WithDescription("Body: `{ name?, country?, priv? }` — each field is updated only if present; " +
                "omitted fields are left unchanged. Deliberately limited to these three fields (no full " +
                "field-by-field editor exists). Returns the updated user row. 404 if no user with this id " +
                "exists; 400 on an invalid new username or if targeting user id 0 (BasilBot)." + AdminKeyNote)
            .WithTags("Users");

        admin.MapPost("/{id:int}/avatar", async (int id, HttpContext context, IOptions<StorageOptions> storage,
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
            .WithSummary("Upload a user's avatar image.")
            .WithDescription("Multipart upload, field name `file`. Replaces any existing avatar for this user id " +
                "(any prior file with a different extension is deleted first). 204 on success." + AdminKeyNote)
            .WithTags("Users");

        admin.MapGet("/{id:int}/avatar", (int id, IOptions<StorageOptions> storage) =>
        {
            var match = Directory.Exists(storage.Value.AvatarsPath)
                ? Directory.EnumerateFiles(storage.Value.AvatarsPath, $"{id}.*").FirstOrDefault()
                : null;
            if (match is null) return Results.NotFound();

            var contentType = Path.GetExtension(match).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
            return Results.File(match, contentType);
        })
            .WithGroupName("basilapi")
            .WithSummary("Download a user's avatar image, by user id.")
            .WithDescription("Serves the raw avatar file uploaded via `POST /user/{id}/avatar`, if any. Unlike " +
                "the `a.<domain>` host's client-facing avatar route, this never falls back to a default image — " +
                "404 if no avatar was ever uploaded for this id. Content-Type is inferred from the file " +
                "extension." + AdminKeyNote)
            .WithTags("Users");

        // Soft delete: zeroes privileges rather than removing the row, so score/social/anticheat
        // history referencing this user's id stays intact — matches how restriction/ban already
        // works in this server (a privilege bit, never a hard delete).
        admin.MapDelete("/{id:int}", async (int id, IUserRepository users, CancellationToken cancellationToken) =>
        {
            if (id == BotBootstrapServiceBotId) return Results.BadRequest(new { error = "Cannot delete BasilBot." });
            if (await users.FetchByIdAsync(id, cancellationToken) is null) return Results.NotFound();

            await users.UpdatePrivilegesAsync(id, 0, cancellationToken);
            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithSummary("Soft-delete a user.")
            .WithDescription("Zeroes the user's privilege bits rather than removing the row, so score/social/" +
                "anticheat history referencing this user id stays intact — the same convention this server " +
                "already uses for restriction/ban. 204 on success, 404 if no user with this id exists, 400 if " +
                "targeting user id 0 (BasilBot)." + AdminKeyNote)
            .WithTags("Users");

        group.MapGet("/user/{id:int}/live", (int id, HttpContext context, IPlayerInputEvents events,
            CancellationToken cancellationToken) =>
        {
            if (id == BotBootstrapServiceBotId) return Results.BadRequest();
            return LiveSseRoutes.HandleInput(context, id, events, cancellationToken);
        })
            .WithGroupName("basilapi")
            .WithSummary("Live raw spectator-input stream (SSE) for one player, by player id.")
            .WithDescription("Server-Sent Events stream (event name `input`) of one player's raw replay-frame " +
                "bytes (base64-encoded), keyed by their numeric `Users.Id` — not scoped to any particular match. " +
                "BasilBot automatically spectates every player from the moment they log in, so this stream is " +
                "live whenever that player is online and playing, tournament match or not. 400 for user id 0 " +
                "(BasilBot itself has no gameplay stream to expose). A nonexistent or offline player id simply " +
                "never receives any frames. Public, no authentication.")
            .WithTags("Live Channels (SSE)");
    }

    // BotBootstrapService.BotId lives in Basil.Application.Services.Bot, which would pull an
    // otherwise-unneeded using into this file for a single constant — inlined instead.
    private const int BotBootstrapServiceBotId = 0;
}

public sealed record CreateUserRequest(string Name, string Password, string? Country, int? Priv);

public sealed record UpdateUserRequest(string? Name, string? Country, int? Priv);
