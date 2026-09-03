using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Services.Users;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Spectating;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Basil.Web.Auth;
using Basil.Web.OpenApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable NotAccessedPositionalProperty.Global

namespace Basil.Web.Routing.Api;

/// <summary>A dedicated logger category marker for the static <see cref="UserRoutes" /> class.</summary>
internal sealed class UserRoutesLog;

/// <summary>
///     Registers the REST endpoints for querying, managing, and spectating users.
/// </summary>
/// <remarks>
///     User reads and the spectate stream are public, while creating, updating, deleting, and avatar
///     management require administrator authorization. Reads accept a username in place of a numeric
///     id and redirect to the canonical numeric form; writes require a numeric id.
/// </remarks>
internal static class UserRoutes
{
	private const string AdminKeyNote = RouteDocs.AdminKeyNote;

	/// <summary>
	///     Registers the `/users` admin CRUD, avatar, and public spectate-stream routes on the `api.` host.
	/// </summary>
	/// <param name="group">The `api.` host route group.</param>
	public static void MapUserRoutes(this RouteGroupBuilder group)
	{
		var admin = group.MapGroup("/users").RequireAuthorization(AdminKeyDefaults.Policy);

		admin.MapGet("", async ([FromQuery] int? page, [FromQuery] int? pageSize, IUserRepository users,
				CancellationToken cancellationToken) =>
			{
				var (p, ps) = Pagination.Normalize(page, pageSize);
				var all = await users.FetchAllAsync(cancellationToken);
				var overqueried = all.Skip((p - 1) * ps).Take(ps + 1).Select(u => u.ToView()).ToList();
				return Results.Json(Pagination.Trim(overqueried, p, ps, all.Count));
			})
			.WithGroupName("basilapi")
			.WithName("listUsers")
			.WithSummary("List users.")
			.WithDescription("""
			                 Returns every user, unfiltered, paginated with `page` (default 1) and `pageSize` (default 50).
			                 """ + AdminKeyNote)
			.WithTags("Users")
			.Produces<PagedResult<UserView>>()
			.WithExample(StatusCodes.Status200OK, new PagedResult<UserView>(1, 50, 1, [SampleUser().ToView()]));

		group.MapGet("/users/{idOrName}", (string idOrName, IUserRepository users,
					CancellationToken cancellationToken) =>
				UserLookup.ResolveAsync(idOrName, users, id => $"/users/{id}",
					id => HandleGetUser(id, users, cancellationToken), cancellationToken))
			.WithGroupName("basilapi")
			.WithName("getUser")
			.WithSummary("Get a user.")
			.WithDescription("""
			                 Returns the user's profile.

			                 A non-numeric `{idOrName}` is resolved via username lookup and redirected to the canonical `/users/{id}` form; a numeric value is served directly.

			                 Returns `404 Not Found` if no user with this id or name exists.
			                 """)
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
			.WithSummary("Create a user.")
			.WithDescription("""
			                 Creates a user with the given `name`, `password`, `country`, and `privilege`.

			                 Returns `400 Bad Request` on an invalid username, or `409 Conflict` if the name is already taken.
			                 """ + AdminKeyNote)
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

		admin.MapPut("/{userId:numericid}", async (int userId, ReplaceUserRequest body, IUserRepository users,
				ILogger<UserRoutesLog> logger, CancellationToken cancellationToken) =>
			{
				if (userId == BotBootstrapService.BotId)
					return Results.BadRequest(new ErrorResponse("Cannot modify BasilBot."));
				var before = await users.FetchByIdAsync(userId, cancellationToken);
				if (before is null) return Results.NotFound(new ErrorResponse("User not found."));

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
			.WithSummary("Replace a user.")
			.WithDescription("""
			                 Replaces the user's `name`, `country`, and `privilege`. These are the only three editable fields, and all three are always applied.

			                 Returns `400 Bad Request` on an invalid new username or when targeting user id 0 (BasilBot), or `404 Not Found` if no user with this id exists.
			                 """ + AdminKeyNote)
			.WithTags("Users")
			.Produces<UserView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, SampleUser().ToView())
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Cannot modify BasilBot."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		admin.MapPatch("/{userId:numericid}", async (int userId, UpdateUserRequest body, IUserRepository users,
				ILogger<UserRoutesLog> logger, CancellationToken cancellationToken) =>
			{
				if (userId == BotBootstrapService.BotId)
					return Results.BadRequest(new ErrorResponse("Cannot modify BasilBot."));
				var before = await users.FetchByIdAsync(userId, cancellationToken);
				if (before is null) return Results.NotFound(new ErrorResponse("User not found."));

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
			.WithSummary("Update a user.")
			.WithDescription("""
			                 Updates the given subset of `name`, `country`, and `privilege`. Omitted fields are left unchanged. These are the only three editable fields.

			                 Returns `400 Bad Request` on an invalid new username or when targeting user id 0 (BasilBot), or `404 Not Found` if no user with this id exists.
			                 """ + AdminKeyNote)
			.WithTags("Users")
			.Produces<UserView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, SampleUser().ToView())
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Cannot modify BasilBot."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		admin.MapPut("/{userId:numericid}/avatar", async (int userId, HttpContext context,
				IOptions<StorageOptions> storage,
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
			.WithSummary("Upload a user avatar.")
			.WithDescription("Multipart upload, field name `file`. Replaces any existing avatar for this user." +
			                 AdminKeyNote)
			.WithTags("Users")
			.WithMultipartFileUpload()
			.Produces<AvatarView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, new AvatarView(7, "https://a.example.test/7"))
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Missing 'file' form field."));

		admin.MapDelete("/{userId:numericid}/avatar", (int userId, IOptions<StorageOptions> storage,
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
			.WithSummary("Reset a user avatar.")
			.WithDescription("""
			                 Removes this user's avatar, if any. Always succeeds, even if no avatar was ever uploaded.

			                 After this, the `a.<domain>` host serves the default avatar for this user again.
			                 """ + AdminKeyNote)
			.WithTags("Users")
			.Produces<AvatarView>()
			.WithExample(StatusCodes.Status200OK, new AvatarView(7, "https://a.example.test/7"));

		group.MapGet("/users/{idOrName}/avatar", (string idOrName, IUserRepository users,
					IOptions<StorageOptions> storage, CancellationToken cancellationToken) =>
				UserLookup.ResolveAsync(idOrName, users, id => $"/users/{id}/avatar",
					id => Task.FromResult(HandleGetAvatar(id, storage)), cancellationToken))
			.WithGroupName("basilapi")
			.WithName("getUserAvatar")
			.WithSummary("Get a user avatar.")
			.WithDescription("""
			                 Serves the raw avatar file uploaded via `PUT /users/{id}/avatar`, if any. Content-Type is inferred from the file extension.

			                 Unlike the `a.<domain>` host's client-facing avatar route, this never falls back to a default image.

			                 A non-numeric `{idOrName}` is resolved via username lookup and redirected to the canonical form.

			                 Returns `404 Not Found` if no avatar was ever uploaded for this user.
			                 """)
			.WithTags("Users")
			.ProducesProblem(StatusCodes.Status404NotFound);

		// Soft delete: stamps DeletedAt and zeroes privileges (defense in depth) rather than removing
		// the row, so score/social/anticheat history referencing this user's id stays intact, and the
		// name stays reserved (Users_Name_uindex/Users_SafeName_uindex) so nobody can register it
		// again. DeletedAt, not the zeroed privilege, is the authoritative "is this user deleted"
		// signal everywhere else in the codebase (login, IRC login) -- see IUserRepository.SoftDeleteAsync.
		admin.MapDelete("/{userId:numericid}",
				async (int userId, IUserRepository users, ILogger<UserRoutesLog> logger,
					CancellationToken cancellationToken) =>
				{
					if (userId == BotBootstrapService.BotId)
						return Results.BadRequest(new ErrorResponse("Cannot delete BasilBot."));
					if (await users.FetchByIdAsync(userId, cancellationToken) is null)
						return Results.NotFound(new ErrorResponse("User not found."));

					await users.SoftDeleteAsync(userId, DateTimeOffset.UtcNow, cancellationToken);
					await users.UpdatePrivilegesAsync(userId, 0, cancellationToken);
					var deleted = await users.FetchByIdAsync(userId, cancellationToken);
					logger.LogInformation("User deleted via admin API: UserId={UserId}", userId);
					return Results.Json(deleted!.ToView());
				})
			.WithGroupName("basilapi")
			.WithName("deleteUser")
			.WithSummary("Delete a user.")
			.WithDescription("""
			                 Soft-deletes the user and returns the updated row, with `deletedAt` now set. Score, social, and anticheat history referencing this user stays intact, and the username stays reserved -- nobody can register it again. A deleted user can no longer log in.

			                 Returns `400 Bad Request` when targeting user id 0 (BasilBot), or `404 Not Found` if no user with this id exists.
			                 """ + AdminKeyNote)
			.WithTags("Users")
			.Produces<UserView>()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status200OK, SampleUser().ToView() with { DeletedAt = DateTimeOffset.UnixEpoch })
			.WithExample(StatusCodes.Status400BadRequest, new ErrorResponse("Cannot delete BasilBot."))
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/users/{idOrName}/live", (string idOrName, IUserRepository users, HttpContext context,
					IPlayerInputEvents inputEvents, IPlayerStatusEvents statusEvents,
					ISessionRegistry<GameSession> gameRegistry, CancellationToken cancellationToken) =>
				UserLookup.ResolveAsync(idOrName, users, id => $"/users/{id}/live",
					id => Task.FromResult(HandleGetLive(id, context, inputEvents, statusEvents, gameRegistry,
						cancellationToken)), cancellationToken))
			.WithGroupName("basilapi")
			.WithName("spectateUser")
			.WithSummary("Spectate a user.")
			.WithDescription("""
			                 Server-Sent Events stream of one user's live status and gameplay input.

			                 Each event is one of two types, carried by the SSE `event` field:

			                 - `status`: online/offline and current activity (the full current status first, then on every change)
			                 - `input`: decoded replay-frame bundles -- button state, cursor position, and the trailing scoreframe per bundle -- only while that user is online and playing, tournament match or not

			                 A non-numeric `{idOrName}` is resolved via username lookup and redirected to the canonical form.

			                 Returns `400 Bad Request` for user id 0 (BasilBot has no live stream to expose).
			                 """)
			.WithTags("Users")
			.Produces<PlayerStatusView>()
			.Produces<SpectateFramesEvent>()
			.WithUserLiveExamples()
			.Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
			.WithExample(StatusCodes.Status400BadRequest,
				new ErrorResponse("BasilBot has no live stream to expose."));
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
		return user is null ? Results.NotFound(new ErrorResponse("User not found.")) : Results.Json(user.ToView());
	}

	private static IResult HandleGetAvatar(int userId, IOptions<StorageOptions> storage)
	{
		var match = Directory.Exists(storage.Value.AvatarsPath)
			? Directory.EnumerateFiles(storage.Value.AvatarsPath, $"{userId}.*").FirstOrDefault()
			: null;
		if (match is null) return Results.NotFound(new ErrorResponse("Avatar not found."));

		return Results.File(match, ContentTypes.Resolve(match));
	}

	private static IResult HandleGetLive(int userId, HttpContext context, IPlayerInputEvents inputEvents,
		IPlayerStatusEvents statusEvents, ISessionRegistry<GameSession> gameRegistry,
		CancellationToken cancellationToken)
	{
		if (userId == BotBootstrapService.BotId)
			return LiveSseRoutes.SseError(StatusCodes.Status400BadRequest,
				"BasilBot has no live stream to expose.");
		return LiveSseRoutes.HandleInput(context, userId, inputEvents, statusEvents, gameRegistry, cancellationToken);
	}
}

/// <summary>Request body for `POST /users`: every field is required.</summary>
public sealed record CreateUserRequest(string Name, string Password, Country Country, UserPrivileges Privilege);

/// <summary>Request body for `PUT /users/{userId}`: full replace, every field required.</summary>
public sealed record ReplaceUserRequest(string Name, Country Country, UserPrivileges Privilege);

/// <summary>Request body for `PATCH /users/{userId}`: every field optional, only present ones are applied.</summary>
public sealed record UpdateUserRequest(string? Name = null, Country? Country = null, UserPrivileges? Privilege = null);

/// <summary>Response body for avatar upload/reset.</summary>
public sealed record AvatarView(int UserId, string AvatarUrl);