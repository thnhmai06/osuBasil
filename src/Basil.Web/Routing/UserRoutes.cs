using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Services.Users;
using Basil.Application.Sessions.Spectating;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Basil.Web.Auth;
using Basil.Web.Middleware;
using Basil.Web.OpenApi;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing;

/// <summary>
///     Dedicated <c>ILogger&lt;T&gt;</c> category marker, because <see cref="UserRoutes" /> is static and can't be a
///     type argument.
/// </summary>
internal sealed class UserRoutesLog;

/// <summary>
///     `/users`: admin CRUD surface, except <c>GET /users/{id}/live</c>, which stays public (a
///     direct rename of the old `/spec/{id}` route, blocked for user id 0, since BasilBot has no
///     gameplay stream of its own to spectate). Every read route (`GET /users/{idOrName}`,
///     `GET /users/{idOrName}/avatar`, `GET /users/{idOrName}/live`) also accepts a username in place
///     of the numeric id, resolved via <see cref="UserLookup" /> and 302-redirected to the canonical
///     `/users/{id}` form; write routes (`PUT`/`PATCH`/`DELETE`) keep a strict numeric `{userId:int}`
///     because a redirect can't reliably carry a request body across most HTTP clients/proxies.
///     Block/unblock (`POST`/`DELETE /users/{id}/block/{targetId}`) is dropped entirely, with no
///     replacement.
/// </summary>
internal static class UserRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	// BotBootstrapService.BotId lives in Basil.Application.Services.Bot, which would pull an
	// otherwise-unneeded using into this file for a single constant — inlined instead.
	private const int BotBootstrapServiceBotId = 0;

	/// <summary>
	///     Registers the `/users` admin CRUD, avatar, and public spectate-stream routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapUserRoutes(this RouteGroupBuilder group)
	{
		var admin = group.MapGroup("/users").RequireAuthorization(AdminKeyDefaults.Policy);

		admin.MapGet("", async (IUserRepository users, CancellationToken cancellationToken) =>
				Results.Json((await users.FetchAllAsync(cancellationToken)).Select(u => u.ToView()).ToList()))
			.WithGroupName("basilapi")
			.WithName("listUsers")
			.WithSummary("List Users")
			.WithDescription("Returns every user row as a JSON array, unfiltered and unpaged." + AdminKeyNote)
			.WithTags("Users")
			.Produces<IReadOnlyList<UserView>>()
			.WithExample(StatusCodes.Status200OK, new List<UserView> { SampleUser().ToView() });

		group.MapGet("/users/{idOrName}", (string idOrName, IUserRepository users,
					CancellationToken cancellationToken) =>
				UserLookup.ResolveAsync(idOrName, users, id => $"/users/{id}",
					id => HandleGetUser(id, users, cancellationToken), cancellationToken))
			.WithGroupName("basilapi")
			.WithName("getUser")
			.WithSummary("Get User")
			.WithDescription("A non-numeric `{idOrName}` is resolved via username lookup and 302-redirected " +
			                 "to the canonical `/users/{id}` form; a numeric value is served directly. 404 if no user " +
			                 "with this id/name exists. Public.")
			.WithTags("Users")
			.Produces<UserView>()
			.WithExample(StatusCodes.Status200OK, SampleUser().ToView())
			.ProducesProblem(StatusCodes.Status404NotFound);

		admin.MapPost("", async (CreateUserRequest body, IUserRepository users,
				IPasswordHasher passwordHasher, ILogger<UserRoutesLog> logger, CancellationToken cancellationToken) =>
			{
				if (!User.ValidateUsername(body.Name, out var usernameError))
					return Results.BadRequest(new ErrorResponse(usernameError));

				if (await users.FetchByNameAsync(body.Name, cancellationToken) is not null)
					return Results.Conflict(new ErrorResponse("Username already exists."));

				var passwordMd5 = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(body.Password)));
				var pwBcrypt = passwordHasher.Hash(Encoding.UTF8.GetBytes(passwordMd5));
				var user = await users.CreateAsync(body.Name, pwBcrypt, body.Country, body.Privilege,
					cancellationToken);
				if (user is null) return Results.Conflict(new ErrorResponse("Username already exists."));

				logger.LogInformation("User created via admin API: NewUserId={NewUserId} Username={Username}",
					user.Id, user.Name);
				return Results.Created($"/users/{user.Id}", user.ToView());
			})
			.WithGroupName("basilapi")
			.WithName("createUser")
			.WithSummary("Create User")
			.WithDescription("Body: `{ name, password, country, privilege }`. Every field is required. The " +
			                 "plaintext `password` is MD5'd then bcrypt-hashed server-side, matching the real client's " +
			                 "own hashing convention. 400 on an invalid username, 409 if the name is already taken." +
			                 AdminKeyNote)
			.WithTags("Users")
			.Produces<UserView>(StatusCodes.Status201Created)
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.Produces<ErrorResponse>(StatusCodes.Status409Conflict)
			.WithExample(StatusCodes.Status201Created, SampleUser().ToView())
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("Username must be between 3 and 15 characters."))
			.WithExample(StatusCodes.Status409Conflict, new ErrorResponse("Username already exists."))
			.WithLink(StatusCodes.Status201Created, "GetUser", "getUser", "Look up the newly created user.",
				("idOrName", "$response.body#/data/id"))
			.WithLink(StatusCodes.Status201Created, "ReplaceUser", "replaceUser",
				"Replace the newly created user's editable fields.",
				("userId", "$response.body#/data/id"))
			.WithLink(StatusCodes.Status201Created, "UpdateUser", "updateUser",
				"Partially update the newly created user.",
				("userId", "$response.body#/data/id"))
			.WithLink(StatusCodes.Status201Created, "DeleteUser", "deleteUser", "Soft-delete the newly created user.",
				("userId", "$response.body#/data/id"));

		admin.MapPut("/{userId:int}", async (int userId, ReplaceUserRequest body, IUserRepository users,
				ILogger<UserRoutesLog> logger, CancellationToken cancellationToken) =>
			{
				if (userId == BotBootstrapServiceBotId)
					return Results.BadRequest(new ErrorResponse("Cannot modify BasilBot."));
				var before = await users.FetchByIdAsync(userId, cancellationToken);
				if (before is null) return Results.NotFound();

				if (!User.ValidateUsername(body.Name, out var usernameError))
					return Results.BadRequest(new ErrorResponse(usernameError));

				await users.UpdateNameAsync(userId, body.Name, User.MakeSafeName(body.Name), cancellationToken);
				await users.UpdateCountryAsync(userId, body.Country, cancellationToken);
				await users.UpdatePrivilegesAsync(userId, body.Privilege, cancellationToken);
				logger.LogInformation(
					"User replaced via admin API: UserId={UserId} OldPrivilege={OldPrivilege} NewPrivilege={NewPrivilege} " +
					"NewName={NewName} NewCountry={NewCountry}",
					userId, before.Privilege, body.Privilege, body.Name, body.Country);

				return Results.Json((await users.FetchByIdAsync(userId, cancellationToken))!.ToView());
			})
			.WithGroupName("basilapi")
			.WithName("replaceUser")
			.WithSummary("Replace User")
			.WithDescription("Body: `{ name, country, privilege }`. Every field is required, and all three are " +
			                 "always applied (full replace). Deliberately limited to these three fields (no full " +
			                 "field-by-field editor exists). Returns the updated user row. 404 if no user with this id " +
			                 "exists; 400 on an invalid new username or if targeting user id 0 (BasilBot)." +
			                 AdminKeyNote)
			.WithTags("Users")
			.Produces<UserView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, SampleUser().ToView())
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Cannot modify BasilBot."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		admin.MapPatch("/{userId:int}", async (int userId, UpdateUserRequest body, IUserRepository users,
				ILogger<UserRoutesLog> logger, CancellationToken cancellationToken) =>
			{
				if (userId == BotBootstrapServiceBotId)
					return Results.BadRequest(new ErrorResponse("Cannot modify BasilBot."));
				var before = await users.FetchByIdAsync(userId, cancellationToken);
				if (before is null) return Results.NotFound();

				if (body.Name is not null)
				{
					if (!User.ValidateUsername(body.Name, out var usernameError))
						return Results.BadRequest(new ErrorResponse(usernameError));

					await users.UpdateNameAsync(userId, body.Name, User.MakeSafeName(body.Name), cancellationToken);
				}

				if (body.Country is not null)
					await users.UpdateCountryAsync(userId, body.Country.Value, cancellationToken);

				if (body.Privilege is not null)
					await users.UpdatePrivilegesAsync(userId, body.Privilege.Value, cancellationToken);

				logger.LogInformation(
					"User updated via admin API: UserId={UserId} OldPrivilege={OldPrivilege} NewPrivilege={NewPrivilege} " +
					"NewName={NewName} NewCountry={NewCountry}",
					userId, before.Privilege, body.Privilege, body.Name, body.Country);
				return Results.Json((await users.FetchByIdAsync(userId, cancellationToken))!.ToView());
			})
			.WithGroupName("basilapi")
			.WithName("updateUser")
			.WithSummary("Update User")
			.WithDescription("Body: `{ name?, country?, privilege? }`. Each field is updated only if " +
			                 "present, and omitted fields are left unchanged. Deliberately limited to these three fields (no " +
			                 "full field-by-field editor exists). Returns the updated user row. 404 if no user with this " +
			                 "id exists; 400 on an invalid new username or if targeting user id 0 (BasilBot)." +
			                 AdminKeyNote)
			.WithTags("Users")
			.Produces<UserView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, SampleUser().ToView())
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Cannot modify BasilBot."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		admin.MapPut("/{userId:int}/avatar", async (int userId, HttpContext context, IOptions<StorageOptions> storage,
				IOptions<ServerOptions> serverOptions, ILogger<UserRoutesLog> logger,
				CancellationToken cancellationToken) =>
			{
				if (!context.Request.HasFormContentType)
					return Results.BadRequest(new ErrorResponse("Expected a multipart file upload."));

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

				logger.LogDebug("User avatar uploaded: UserId={UserId}", userId);
				return Results.Json(new AvatarView(userId, $"https://a.{serverOptions.Value.Domain}/{userId}"));
			})
			.WithGroupName("basilapi")
			.WithName("uploadUserAvatar")
			.WithSummary("Upload User Avatar")
			.WithDescription("Multipart upload, field name `file`. Replaces any existing avatar for this user id " +
			                 "(any prior file with a different extension is deleted first)." + AdminKeyNote)
			.WithTags("Users")
			.WithMultipartFileUpload()
			.Produces<AvatarView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new AvatarView(7, "https://a.example.test/7"))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Missing 'file' form field."));

		admin.MapDelete("/{userId:int}/avatar", (int userId, IOptions<StorageOptions> storage,
				IOptions<ServerOptions> serverOptions, ILogger<UserRoutesLog> logger) =>
			{
				if (Directory.Exists(storage.Value.AvatarsPath))
					foreach (var existing in Directory.EnumerateFiles(storage.Value.AvatarsPath, $"{userId}.*"))
						File.Delete(existing);

				logger.LogDebug("User avatar reset: UserId={UserId}", userId);
				return Results.Json(new AvatarView(userId, $"https://a.{serverOptions.Value.Domain}/{userId}"));
			})
			.WithGroupName("basilapi")
			.WithName("resetUserAvatar")
			.WithSummary("Reset User Avatar")
			.WithDescription("Deletes every uploaded avatar file for this user id, if any. Always succeeds, " +
			                 "even if no avatar was ever uploaded (idempotent-delete convention). The `a.<domain>` host's " +
			                 "own avatar route re-checks the filesystem per request, so its default-avatar fallback " +
			                 "reappears immediately." + AdminKeyNote)
			.WithTags("Users")
			.Produces<AvatarView>()
			.WithExample(StatusCodes.Status200OK, new AvatarView(7, "https://a.example.test/7"));

		group.MapGet("/users/{idOrName}/avatar", (string idOrName, IUserRepository users,
					IOptions<StorageOptions> storage, CancellationToken cancellationToken) =>
				UserLookup.ResolveAsync(idOrName, users, id => $"/users/{id}/avatar",
					id => Task.FromResult(HandleGetAvatar(id, storage)), cancellationToken))
			.WithGroupName("basilapi")
			.WithName("getUserAvatar")
			.WithSummary("Get User Avatar")
			.WithDescription("Serves the raw avatar file uploaded via `POST /users/{id}/avatar`, if any. Unlike " +
			                 "the `a.<domain>` host's client-facing avatar route, this never falls back to a default image: " +
			                 "404 if no avatar was ever uploaded for this id. Content-Type is inferred from the file " +
			                 "extension. A non-numeric `{idOrName}` is resolved via username lookup and 302-redirected to " +
			                 "the canonical form. Public.")
			.WithTags("Users")
			.ProducesProblem(StatusCodes.Status404NotFound);

		// Soft delete: zeroes privileges rather than removing the row, so score/social/anticheat
		// history referencing this user's id stays intact — matches how restriction/ban already
		// works in this server (a privilege bit, never a hard delete).
		admin.MapDelete("/{userId:int}",
				async (int userId, IUserRepository users, ILogger<UserRoutesLog> logger,
					CancellationToken cancellationToken) =>
				{
					if (userId == BotBootstrapServiceBotId)
						return Results.BadRequest(new ErrorResponse("Cannot delete BasilBot."));
					if (await users.FetchByIdAsync(userId, cancellationToken) is null) return Results.NotFound();

					await users.UpdatePrivilegesAsync(userId, 0, cancellationToken);
					var deleted = await users.FetchByIdAsync(userId, cancellationToken);
					logger.LogInformation("User deleted via admin API: UserId={UserId}", userId);
					return Results.Json(deleted!.ToView());
				})
			.WithGroupName("basilapi")
			.WithName("deleteUser")
			.WithSummary("Delete User")
			.WithDescription("Zeroes the user's privilege bits rather than removing the row, so score/social/" +
			                 "anticheat history referencing this user id stays intact (the same convention this server " +
			                 "already uses for restriction/ban). Returns the soft-deleted user row (privilege now zero). " +
			                 "404 if no user with this id exists, 400 if targeting user id 0 (BasilBot)." +
			                 AdminKeyNote)
			.WithTags("Users")
			.Produces<UserView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, SampleUser().ToView() with { Privilege = 0 })
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Cannot delete BasilBot."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/users/{idOrName}/live", (string idOrName, IUserRepository users, HttpContext context,
					IPlayerInputEvents events, CancellationToken cancellationToken) =>
				UserLookup.ResolveAsync(idOrName, users, id => $"/users/{id}/live",
					id => Task.FromResult(HandleGetLive(id, context, events, cancellationToken)), cancellationToken))
			.WithGroupName("basilapi")
			.WithMetadata(SseEndpointMarker.Instance)
			.WithName("spectateUser")
			.WithSummary("Spectate User")
			.WithDescription("Server-Sent Events stream (event name `frames`) of one userSession's decoded " +
			                 "replay-frame bundles (button state, cursor position, and the trailing scoreframe per " +
			                 "bundle), keyed by their numeric `Users.Id`, not scoped to any particular match. " +
			                 "BasilBot automatically spectates every userSession from the moment they log in, so this stream is " +
			                 "live whenever that userSession is online and playing, tournament match or not. 400 for user id 0 " +
			                 "(BasilBot itself has no gameplay stream to expose). A nonexistent or offline userSession id simply " +
			                 "never receives any frames. A non-numeric `{idOrName}` is resolved via username lookup and " +
			                 "302-redirected to the canonical form. Public, no authentication.")
			.WithTags("Users")
			.Produces<SpectateFramesEvent>()
			.WithExample(StatusCodes.Status200OK, new SpectateFramesEvent(new UserBrief(7, "Alice", Country.Us),
				ReplayAction.Standard, 0, [new ReplayFrame(Keys.Left1, TaikoByte.None, 100.5f, 200.25f, 1000)],
				new ScoreFrame(1000, 0, 10, 2, 1, 0, 0, 0, 123456, 50, 12, true, 100, 0, false)))
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("BasilBot has no gameplay stream to expose."));
	}

	private static User SampleUser()
	{
		return new User(7, "Alice", Country.Vn, UserPrivileges.Unrestricted | UserPrivileges.Verified,
			DateTimeOffset.UnixEpoch);
	}

	private static async Task<IResult> HandleGetUser(int userId, IUserRepository users,
		CancellationToken cancellationToken)
	{
		var user = await users.FetchByIdAsync(userId, cancellationToken);
		return user is null ? Results.NotFound() : Results.Json(user.ToView());
	}

	private static IResult HandleGetAvatar(int userId, IOptions<StorageOptions> storage)
	{
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
		if (userId == BotBootstrapServiceBotId)
			return LiveSseRoutes.SseError(StatusCodes.Status400BadRequest,
				"BasilBot has no gameplay stream to expose.");
		return LiveSseRoutes.HandleInput(context, userId, events, cancellationToken);
	}
}

/// <summary>Body for `POST /users`: every field is required.</summary>
public sealed record CreateUserRequest(string Name, string Password, Country Country, UserPrivileges Privilege);

/// <summary>PUT: full replace, every field required.</summary>
public sealed record ReplaceUserRequest(string Name, Country Country, UserPrivileges Privilege);

/// <summary>PATCH: every field optional, only present ones are applied.</summary>
public sealed record UpdateUserRequest(string? Name = null, Country? Country = null, UserPrivileges? Privilege = null);

/// <summary>Response body for avatar upload/reset.</summary>
public sealed record AvatarView(int UserId, string AvatarUrl);