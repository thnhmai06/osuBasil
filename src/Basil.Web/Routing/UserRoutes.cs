using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Web.Auth;
using Basil.Web.Middleware;
using Basil.Web.OpenApi;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing;

/// <summary>
///     `/users` — admin CRUD surface, except <c>GET /users/{id}/live</c>, which stays public — a
///     direct rename of the old `/spec/{id}` route, blocked for user id 0 (BasilBot has no gameplay
///     stream of its own to spectate). Every read route (`GET /users/{idOrName}`,
///     `GET /users/{idOrName}/avatar`, `GET /users/{idOrName}/live`) also accepts a username in place
///     of the numeric id, resolved via <see cref="UserLookup" /> and 302-redirected to the canonical
///     `/users/{id}` form — write routes (`PUT`/`PATCH`/`DELETE`) keep a strict numeric `{userId:int}`
///     since a redirect can't reliably carry a request body across most HTTP clients/proxies.
///     Block/unblock (`POST`/`DELETE /users/{id}/block/{targetId}`) is dropped entirely — no
///     replacement.
/// </summary>
internal static class UserRoutes
{
    private const string AdminKeyNote = RouteDocs.AdminKeyNote;

    // BotBootstrapService.BotId lives in Basil.Application.Services.Bot, which would pull an
    // otherwise-unneeded using into this file for a single constant — inlined instead.
    private const int BotBootstrapServiceBotId = 0;

    public static void MapUserRoutes(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/users").RequireAuthorization(AdminKeyDefaults.Policy);

        admin.MapGet("", async (IUserRepository users, CancellationToken cancellationToken) =>
            Results.Json(await users.FetchAllAsync(cancellationToken)))
            .WithGroupName("basilapi")
            .WithName("listUsers")
            .WithSummary("List Users")
            .WithDescription("Returns every user row as a JSON array, unfiltered and unpaged." + AdminKeyNote)
            .WithTags("Users")
            .Produces<IReadOnlyList<User>>()
            .WithExample(StatusCodes.Status200OK, new List<User> { SampleUser() });

        // Public outer route so a non-numeric {idOrName} can be accepted at all — the numeric branch
        // still enforces the admin policy manually (RequireAuthorization can't attach to a route
        // template shared with the public username-redirect branch).
        group.MapGet("/users/{idOrName}", (string idOrName, IUserRepository users, HttpContext context,
            CancellationToken cancellationToken) =>
            UserLookup.ResolveAsync(idOrName, users, id => $"/users/{id}",
                id => HandleGetUser(id, context, users, cancellationToken), cancellationToken))
            .WithGroupName("basilapi")
            .WithName("getUser")
            .WithSummary("Get User")
            .WithDescription("A non-numeric `{idOrName}` is resolved via username lookup and 302-redirected " +
                "to the canonical `/users/{id}` form; a numeric value is served directly. 404 if no user " +
                "with this id/name exists." + AdminKeyNote)
            .WithTags("Users")
            .Produces<User>()
            .WithExample(StatusCodes.Status200OK, SampleUser())
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapPost("", async (CreateUserRequest body, IUserRepository users,
            IPasswordHasher passwordHasher, CancellationToken cancellationToken) =>
        {
            if (!User.ValidateUsername(body.Name, out var usernameError))
                return Results.BadRequest(new ErrorResponse(usernameError));

            if (await users.FetchByNameAsync(body.Name, cancellationToken) is not null)
                return Results.Conflict(new ErrorResponse("Username already exists."));

            var passwordMd5 = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(body.Password)));
            var pwBcrypt = passwordHasher.Hash(Encoding.UTF8.GetBytes(passwordMd5));
            var user = await users.CreateAsync(body.Name, pwBcrypt, body.Country ?? "xx",
                (UserPrivileges?)body.Priv, cancellationToken);
            return user is null
                ? Results.Conflict(new ErrorResponse("Username already exists."))
                : Results.Json(user);
        })
            .WithGroupName("basilapi")
            .WithName("createUser")
            .WithSummary("Create User")
            .WithDescription("Body: `{ name, password, country?, priv? }` (`country` defaults to `\"xx\"`, " +
                "`priv` to the server's default privileges if omitted). The plaintext `password` is MD5'd then " +
                "bcrypt-hashed server-side, matching the real client's own hashing convention. 400 on an " +
                "invalid username, 409 if the name is already taken." + AdminKeyNote)
            .WithTags("Users")
            .Produces<User>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .WithExample(StatusCodes.Status200OK, SampleUser())
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Username must be between 3 and 15 characters."))
            .WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Username already exists."));

        Func<int, UpdateUserRequest, IUserRepository, CancellationToken, Task<IResult>> updateUserHandler =
            async (userId, body, users, cancellationToken) =>
        {
            if (userId == BotBootstrapServiceBotId) return Results.BadRequest(new ErrorResponse("Cannot modify BasilBot."));
            if (await users.FetchByIdAsync(userId, cancellationToken) is null) return Results.NotFound();

            if (body.Name is not null)
            {
                if (!User.ValidateUsername(body.Name, out var usernameError))
                    return Results.BadRequest(new ErrorResponse(usernameError));

                await users.UpdateNameAsync(userId, body.Name, User.MakeSafeName(body.Name), cancellationToken);
            }

            if (body.Country is not null)
                await users.UpdateCountryAsync(userId, body.Country, cancellationToken);
            if (body.Priv is not null)
                await users.UpdatePrivilegesAsync(userId, (UserPrivileges)body.Priv.Value, cancellationToken);

            return Results.Json(await users.FetchByIdAsync(userId, cancellationToken));
        };

        const string updateUserDescription = "Body: `{ name?, country?, priv? }` — each field is updated only if present; " +
            "omitted fields are left unchanged. Deliberately limited to these three fields (no full " +
            "field-by-field editor exists). Returns the updated user row. 404 if no user with this id " +
            "exists; 400 on an invalid new username or if targeting user id 0 (BasilBot).";

        admin.MapPut("/{userId:int}", updateUserHandler)
            .WithGroupName("basilapi")
            .WithName("replaceUser")
            .WithSummary("Replace User")
            .WithDescription(updateUserDescription + AdminKeyNote)
            .WithTags("Users")
            .Produces<User>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status200OK, SampleUser())
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Cannot modify BasilBot."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapPatch("/{userId:int}", updateUserHandler)
            .WithGroupName("basilapi")
            .WithName("updateUser")
            .WithSummary("Update User")
            .WithDescription(updateUserDescription + AdminKeyNote)
            .WithTags("Users")
            .Produces<User>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status200OK, SampleUser())
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Cannot modify BasilBot."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapPut("/{userId:int}/avatar", async (int userId, HttpContext context, IOptions<StorageOptions> storage,
            CancellationToken cancellationToken) =>
        {
            if (!context.Request.HasFormContentType) return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new ErrorResponse("Missing 'file' form field."));

            var extension = Path.GetExtension(file.FileName);
            Directory.CreateDirectory(storage.Value.AvatarsPath);
            foreach (var existing in Directory.EnumerateFiles(storage.Value.AvatarsPath, $"{userId}.*"))
                File.Delete(existing);

            var destination = Path.Combine(storage.Value.AvatarsPath, $"{userId}{extension}");
            await using var fileStream = File.Create(destination);
            await file.CopyToAsync(fileStream, cancellationToken);

            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithName("uploadUserAvatar")
            .WithSummary("Upload User Avatar")
            .WithDescription("Multipart upload, field name `file`. Replaces any existing avatar for this user id " +
                "(any prior file with a different extension is deleted first). 204 on success." + AdminKeyNote)
            .WithTags("Users")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Missing 'file' form field."));

        admin.MapDelete("/{userId:int}/avatar", (int userId, IOptions<StorageOptions> storage) =>
        {
            if (Directory.Exists(storage.Value.AvatarsPath))
                foreach (var existing in Directory.EnumerateFiles(storage.Value.AvatarsPath, $"{userId}.*"))
                    File.Delete(existing);

            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithName("resetUserAvatar")
            .WithSummary("Reset User Avatar")
            .WithDescription("Deletes every uploaded avatar file for this user id, if any. Always 204, even if " +
                "no avatar was ever uploaded (idempotent-delete convention). The `a.<domain>` host's own avatar " +
                "route re-checks the filesystem per request, so its default-avatar fallback reappears " +
                "immediately." + AdminKeyNote)
            .WithTags("Users")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/users/{idOrName}/avatar", (string idOrName, IUserRepository users, HttpContext context,
            IOptions<StorageOptions> storage, CancellationToken cancellationToken) =>
            UserLookup.ResolveAsync(idOrName, users, id => $"/users/{id}/avatar",
                id => Task.FromResult(HandleGetAvatar(id, context, storage)), cancellationToken))
            .WithGroupName("basilapi")
            .WithName("getUserAvatar")
            .WithSummary("Get User Avatar")
            .WithDescription("Serves the raw avatar file uploaded via `POST /users/{id}/avatar`, if any. Unlike " +
                "the `a.<domain>` host's client-facing avatar route, this never falls back to a default image — " +
                "404 if no avatar was ever uploaded for this id. Content-Type is inferred from the file " +
                "extension. A non-numeric `{idOrName}` is resolved via username lookup and 302-redirected to " +
                "the canonical form." + AdminKeyNote + " (Enforced manually in the handler, same deviation as " +
                "`GET /users/{idOrName}`.)")
            .WithTags("Users")
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Soft delete: zeroes privileges rather than removing the row, so score/social/anticheat
        // history referencing this user's id stays intact — matches how restriction/ban already
        // works in this server (a privilege bit, never a hard delete).
        admin.MapDelete("/{userId:int}", async (int userId, IUserRepository users, CancellationToken cancellationToken) =>
        {
            if (userId == BotBootstrapServiceBotId) return Results.BadRequest(new ErrorResponse("Cannot delete BasilBot."));
            if (await users.FetchByIdAsync(userId, cancellationToken) is null) return Results.NotFound();

            await users.UpdatePrivilegesAsync(userId, 0, cancellationToken);
            return Results.NoContent();
        })
            .WithGroupName("basilapi")
            .WithName("deleteUser")
            .WithSummary("Delete User")
            .WithDescription("Zeroes the user's privilege bits rather than removing the row, so score/social/" +
                "anticheat history referencing this user id stays intact — the same convention this server " +
                "already uses for restriction/ban. 204 on success, 404 if no user with this id exists, 400 if " +
                "targeting user id 0 (BasilBot)." + AdminKeyNote)
            .WithTags("Users")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Cannot delete BasilBot."))
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/users/{idOrName}/live", (string idOrName, IUserRepository users, HttpContext context,
            IPlayerInputEvents events, CancellationToken cancellationToken) =>
            UserLookup.ResolveAsync(idOrName, users, id => $"/users/{id}/live",
                id => Task.FromResult(HandleGetLive(id, context, events, cancellationToken)), cancellationToken))
            .WithGroupName("basilapi")
            .WithMetadata(SseEndpointMarker.Instance)
            .WithName("getUserLiveStream")
            .WithSummary("Get User Live Stream")
            .WithDescription("Server-Sent Events stream (event name `input`) of one player's raw replay-frame " +
                "bytes (base64-encoded), keyed by their numeric `Users.Id` — not scoped to any particular match. " +
                "BasilBot automatically spectates every player from the moment they log in, so this stream is " +
                "live whenever that player is online and playing, tournament match or not. 400 for user id 0 " +
                "(BasilBot itself has no gameplay stream to expose). A nonexistent or offline player id simply " +
                "never receives any frames. A non-numeric `{idOrName}` is resolved via username lookup and " +
                "302-redirected to the canonical form. Public, no authentication.")
            .WithTags("Users")
            .Produces<PlayerInputFrame>()
            .WithExample(StatusCodes.Status200OK, new PlayerInputFrame(new UserBrief(7, "Alice", "us"), "QUJD"))
            .Produces(StatusCodes.Status400BadRequest);
    }

    private static User SampleUser()
    {
        return new User(7, "Alice", Country.Vn, UserPrivileges.Unrestricted | UserPrivileges.Verified,
            DateTimeOffset.UnixEpoch);
    }

    private static async Task<IResult> HandleGetUser(int userId, HttpContext context, IUserRepository users,
        CancellationToken cancellationToken)
    {
        if (!context.User.IsInRole(AdminKeyDefaults.Role)) return Results.Unauthorized();

        var user = await users.FetchByIdAsync(userId, cancellationToken);
        return user is null ? Results.NotFound() : Results.Json(user);
    }

    private static IResult HandleGetAvatar(int userId, HttpContext context, IOptions<StorageOptions> storage)
    {
        if (!context.User.IsInRole(AdminKeyDefaults.Role)) return Results.Unauthorized();

        var match = Directory.Exists(storage.Value.AvatarsPath)
            ? Directory.EnumerateFiles(storage.Value.AvatarsPath, $"{userId}.*").FirstOrDefault()
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
    }

    private static IResult HandleGetLive(int userId, HttpContext context, IPlayerInputEvents events,
        CancellationToken cancellationToken)
    {
        if (userId == BotBootstrapServiceBotId) return Results.BadRequest();
        return LiveSseRoutes.HandleInput(context, userId, events, cancellationToken);
    }
}

public sealed record CreateUserRequest(string Name, string Password, string? Country, int? Priv);

public sealed record UpdateUserRequest(string? Name, string? Country, int? Priv);
