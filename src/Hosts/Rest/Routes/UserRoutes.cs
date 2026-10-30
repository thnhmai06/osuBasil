using System.Text;
using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Host.Rest.Http;
using Basil.Host.Shared.Views;
using Basil.Host.Rest.Sse;
using Basil.Application.Auth.Persistence;
using Basil.Application.Common.Common;

namespace Basil.Host.Rest.Routes;

/// <summary>Accounts, their sessions, friends, blocks and restrictions.</summary>
internal static class UserRoutes
{
	/// <summary>The fields of an account that can change; absent fields stay as they are.</summary>
	public sealed record PatchUserRequest(string? Name, string? Country, string? Away, bool? PmPrivate,
		ulong? Permissions);

	/// <summary>A password change: the current password is needed for one's own account only.</summary>
	public sealed record PasswordRequest(string? Current, string? New, bool? IncludeCurrent);

	/// <summary>A new account made by staff.</summary>
	public sealed record CreateUserRequest(string? Username, string? Password, string? Country, ulong? Permissions);

	/// <summary>A request to close all sessions of one's own account.</summary>
	public sealed record CloseSessionsRequest(bool? KeepCurrent);

	/// <summary>A request to open an IRC key.</summary>
	public sealed record IrcKeyRequest(string? Label);

	/// <summary>A new IRC key; the key is shown only here.</summary>
	public sealed record IrcKeyResponse(Guid SessionId, string Key);

	/// <summary>A new restriction.</summary>
	public sealed record RestrictRequest(ulong Permissions, DateTimeOffset? EndsAt, string? Reason);

	public static void MapUsers(this RouteGroupBuilder v1)
	{
		var users = v1.MapGroup("/users").WithTags("Users");
		var token = users.MapGroup("").RequireToken();

		users.MapGet("/{id:int}", GetAsync).WithSummary("Retrieve an account.")
			.WithDescription("Includes the granted and the effective permissions and the restrictions in force.");
		users.MapGet("/{id:int}/restrictions", RestrictionsAsync)
			.WithSummary("List the restrictions an account has had, current and past.");

		token.MapSpectate();
		token.MapGet("/", SearchAsync).WithSummary("Search accounts.")
			.WithDescription("`query` uses the user search syntax. Paged with `offset` and `limit`.");
		token.MapPost("/", CreateAsync).WithSummary("Create an account.")
			.WithDescription("For staff only. Accounts are not created by themselves through this API.");
		token.MapPatch("/{id:int}", PatchAsync).WithSummary("Change an account.");
		token.MapPut("/{id:int}/password", PasswordAsync).WithSummary("Change the password of an account.")
			.WithDescription("""
				For one's own account the current password is required and every other session is closed; with
				`includeCurrent` the calling session is closed too. Staff change another account's password without
				the current one and every session of that account is closed.

				Signing an osu! client out only makes the device ask for the password again; changing the password
				is what keeps the client out. While the profiles area is locked, only staff can change a password.
				""");
		token.MapDelete("/{id:int}", DeleteAsync).WithSummary("Delete an account.");
		token.MapPut("/{id:int}/avatar", PutAvatarAsync).DisableAntiforgery()
			.WithSummary("Upload the avatar of an account as an image in the `file` field.")
			.WithDescription("""
				While the profiles area is locked only staff can change an avatar. The avatar is shown on the avatar host.

				The file must be a PNG or JPEG picture within the configured size and pixel limits. Content that is not
				such a picture, or that exceeds a limit, is refused with `422`.
				""");
		token.MapDelete("/{id:int}/avatar", DeleteAvatarAsync).WithSummary("Remove the avatar of an account.");

		token.MapGet("/{id:int}/sessions", ListSessionsAsync).WithSummary("List the sessions of an account.")
			.WithDescription("Filter by `state` (all, online, offline) and by `types`, a bitmask of the connection kinds.");
		token.MapGet("/{id:int}/sessions/{sid:guid}", GetSessionAsync).WithSummary("Retrieve one session of an account.");
		token.MapDelete("/{id:int}/sessions/{sid:guid}", CloseSessionAsync)
			.WithSummary("Close one session of an account.")
			.WithDescription("Closing an osu! session only makes the device ask for the password again; change the password to keep the client out.");
		token.MapDelete("/{id:int}/sessions", CloseSessionsAsync).WithSummary("Close every session of an account.");
		token.MapPost("/{id:int}/sessions/irc", OpenIrcAsync).WithSummary("Create an IRC key for one device.")
			.WithDescription("The key is shown only in this response. Close the key's session to revoke it.");

		token.MapGet("/{id:int}/friends", FriendsAsync).WithSummary("List the friends of the calling account.");
		token.MapPut("/{id:int}/friends/{otherId:int}", (HttpContext c, int id, int otherId) =>
			RelationAsync(c, id, otherId, s => s.Relationships.AddFriendAsync(c.RequestAborted)))
			.WithSummary("Add a friend.");
		token.MapDelete("/{id:int}/friends/{otherId:int}", (HttpContext c, int id, int otherId) =>
			RelationAsync(c, id, otherId, s => s.Relationships.RemoveFriendAsync(c.RequestAborted)))
			.WithSummary("Remove a friend.");
		token.MapGet("/{id:int}/blocks", BlocksAsync).WithSummary("List the accounts the calling account blocked.");
		token.MapPut("/{id:int}/blocks/{otherId:int}", (HttpContext c, int id, int otherId) =>
			RelationAsync(c, id, otherId, s => s.Relationships.BlockAsync(c.RequestAborted)))
			.WithSummary("Block an account.");
		token.MapDelete("/{id:int}/blocks/{otherId:int}", (HttpContext c, int id, int otherId) =>
			RelationAsync(c, id, otherId, s => s.Relationships.UnblockAsync(c.RequestAborted)))
			.WithSummary("Unblock an account.");

		token.MapPost("/{id:int}/restrictions", RestrictAsync).WithSummary("Restrict an account.");
		token.MapPost("/{id:int}/restrictions/{rid:int}/lift", LiftAsync)
			.WithSummary("End a restriction now and keep it in the history.");
		token.MapDelete("/{id:int}/restrictions/{rid:int}", DeleteRestrictionAsync)
			.WithSummary("End a restriction and remove it from the history.");
	}

	private static async Task<IResult> GetAsync(int id, IUserRepository users, IRestrictionRepository restrictions,
		UserRegistry registry, TimeProvider clock, CancellationToken cancellationToken)
	{
		var record = await users.GetAsync(id, cancellationToken);
		return record is null || record.Value.DeletedAt is not null
			? Api.NotFound("No such user.")
			: Api.Ok(UserView.Of(registry, record, clock, await restrictions.ListAsync(record, cancellationToken)));
	}

	private static async Task<IResult> RestrictionsAsync(int id, IUserRepository users, IRestrictionRepository restrictions,
		CancellationToken cancellationToken)
	{
		var record = await users.GetAsync(id, cancellationToken);
		if (record is null || record.Value.DeletedAt is not null) return Api.NotFound("No such user.");

		return Api.Ok((await restrictions.ListAsync(record, cancellationToken)).Select(RestrictionView.Of).ToList());
	}

	private static async Task<IResult> SearchAsync(string? query, int? offset, int? limit, IUserRepository users,
		UserRegistry registry, TimeProvider clock, CancellationToken cancellationToken)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var found = await users.ListAsync(UserQuery.Parse(query ?? string.Empty), page!, cancellationToken);
		return Api.Paged(new Page<UserView>([.. found.Items.Select(u => UserView.Of(registry, u, clock))], found.Total), page!);
	}

	private static async Task<IResult> CreateAsync(HttpContext context, CreateUserRequest request, UserRegistry registry,
		TimeProvider clock)
	{
		if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
			return Api.Invalid("username and password are required.");

		var data = new UserData
		{
			Name = request.Username,
			Permissions = request.Permissions is { } p ? (UserPermissions)p : UserPermissionsGroups.Default
		};
		if (Targets.ParseCountry(request.Country) is { } country) data.Country = country;

		var (record, result) = await registry.ScopeTo(context.Connection()).CreateAccountAsync(data,
			new Md5(Encoding.UTF8.GetBytes(request.Password)), context.RequestAborted);
		return result.IsOk ? Api.Created(UserView.Of(registry, record!, clock)) : result.Refusal();
	}

	private static async Task<IResult> PatchAsync(HttpContext context, int id, PatchUserRequest request,
		UserRegistry registry, TimeProvider clock)
	{
		if (await context.ScopeAsync(id) is not { } scoped) return Api.NotFound("No such user.");

		var ct = context.RequestAborted;
		if (await PatchNameAndCountryAsync(scoped, request, ct) is { } refusal) return refusal;
		if (await PatchSelfAndPermissionsAsync(scoped, request, ct) is { } denied) return denied;

		return Api.Ok(UserView.Of(registry, scoped.User.Record, clock), "Updated successfully");
	}

	private static async Task<IResult?> PatchNameAndCountryAsync(ScopedUser scoped, PatchUserRequest request, CancellationToken ct)
	{
		if (request.Name is not null && await scoped.Profile.RenameAsync(request.Name, ct) is { IsOk: false } renamed)
			return renamed.Refusal();

		if (request.Country is null) return null;
		if (Targets.ParseCountry(request.Country) is not { } country)
			return Api.Invalid("country must be a two-letter code.");
		return await scoped.Profile.SetCountryAsync(country, ct) is { IsOk: false } changed ? changed.Refusal() : null;
	}

	private static async Task<IResult?> PatchSelfAndPermissionsAsync(ScopedUser scoped, PatchUserRequest request, CancellationToken ct)
	{
		if (request.Away is not null && !scoped.Profile.SetAway(request.Away.Length == 0 ? null : request.Away))
			return Api.Error(StatusCodes.Status403Forbidden, "notSelf", "Only the account itself can do this.");
		if (request.PmPrivate is { } pm && !scoped.Profile.SetPmPrivate(pm))
			return Api.Error(StatusCodes.Status403Forbidden, "notSelf", "Only the account itself can do this.");
		if (request.Permissions is { } permissions &&
		    await scoped.Permissions.SetAsync((UserPermissions)permissions, ct) is { IsOk: false } set)
			return set.Refusal();

		return null;
	}

	private static async Task<IResult> PasswordAsync(HttpContext context, int id, PasswordRequest request)
	{
		if (string.IsNullOrEmpty(request.New)) return Api.Invalid("new is required.");
		if (await context.ScopeAsync(id) is not { } scoped) return Api.NotFound("No such user.");

		Md5? current = request.Current is null ? (Md5?)null : new Md5(Encoding.UTF8.GetBytes(request.Current));
		return (await scoped.Credentials.ChangePasswordAsync(new Md5(Encoding.UTF8.GetBytes(request.New)), current,
			request.IncludeCurrent ?? false, context.RequestAborted)).Answer();
	}

	private static async Task<IResult> DeleteAsync(HttpContext context, int id)
	{
		return await context.ScopeAsync(id) is not { } scoped
			? Api.NotFound("No such user.")
			: (await scoped.DeleteAsync(context.RequestAborted)).Answer();
	}

	private static async Task<IResult> PutAvatarAsync(HttpContext context, int id, ISettingsRepository settings)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form with a file field is required.");

		var form = await context.Request.ReadFormAsync(context.RequestAborted);
		if (form.Files.GetFile("file") is not { } file) return Api.Invalid("The file field is missing.");
		if (file.Length > (await settings.GetAsync(context.RequestAborted)).Uploads.AvatarMaxSize)
			return Api.Invalid("The avatar is too large.", "tooLarge");
		if (await context.ScopeAsync(id) is not { } scoped) return Api.NotFound("No such user.");

		await using var stream = file.OpenReadStream();
		return (await scoped.Profile.SetAvatarAsync(stream, context.RequestAborted)).Answer();
	}

	private static async Task<IResult> DeleteAvatarAsync(HttpContext context, int id)
	{
		return await context.ScopeAsync(id) is not { } scoped
			? Api.NotFound("No such user.")
			: (await scoped.Profile.RemoveAvatarAsync(context.RequestAborted)).Answer();
	}

	private static async Task<IResult> ListSessionsAsync(HttpContext context, int id, string? state, int? types,
		int? offset, int? limit)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var sessionState = (state ?? "all").ToLowerInvariant() switch
		{
			"all" => (SessionState?)SessionState.Any,
			"online" => SessionState.Active,
			"offline" => SessionState.Ended,
			_ => null
		};
		if (sessionState is null) return Api.Invalid("state must be all, online or offline.");

		var kinds = types is null
			? null
			: Enum.GetValues<SessionType>().Where(t => (types.Value & (int)t) != 0).ToList();
		if (await context.ScopeAsync(id) is not { } scoped) return Api.NotFound("No such user.");

		var (result, sessions) = await scoped.Sessions.ListAsync(page!, sessionState.Value, kinds, context.RequestAborted);
		if (!result.IsOk) return result.Refusal();

		return Api.Paged(new Page<SessionView>([.. sessions!.Items.Select(SessionView.Of)], sessions.Total), page!);
	}

	private static async Task<IResult> GetSessionAsync(HttpContext context, int id, Guid sid)
	{
		if (await context.ScopeAsync(id) is not { } scoped) return Api.NotFound("No such user.");

		var (result, session) = await scoped.Sessions.GetAsync(sid, context.RequestAborted);
		return result.IsOk ? Api.Ok(SessionView.Of(session!)) : result.Refusal();
	}

	private static async Task<IResult> CloseSessionAsync(HttpContext context, int id, Guid sid)
	{
		return await context.ScopeAsync(id) is not { } scoped
			? Api.NotFound("No such user.")
			: (await scoped.Sessions.CloseAsync(sid, context.RequestAborted)).Answer();
	}

	private static async Task<IResult> CloseSessionsAsync(HttpContext context, int id)
	{
		var request = await context.OptionalBodyAsync<CloseSessionsRequest>();
		return await context.ScopeAsync(id) is not { } scoped
			? Api.NotFound("No such user.")
			: (await scoped.Sessions.CloseAllAsync(request?.KeepCurrent ?? true, context.RequestAborted)).Answer();
	}

	private static async Task<IResult> OpenIrcAsync(HttpContext context, int id, IrcKeyRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Label)) return Api.Invalid("label is required.");
		if (await context.ScopeAsync(id) is not { } scoped) return Api.NotFound("No such user.");

		var (result, key, session) = await scoped.Sessions.OpenIrcAsync(request.Label, context.RequestAborted);
		return result.IsOk ? Api.Created(new IrcKeyResponse(session!.Id, key!)) : result.Refusal();
	}

	private static async Task<IResult> FriendsAsync(HttpContext context, int id)
	{
		if (await context.ScopeAsync(id) is not { } scoped) return Api.NotFound("No such user.");

		var (result, list) = await scoped.Relationships.FriendsAsync(context.RequestAborted);
		return result.IsOk ? Api.Ok(list.Select(UserRef.Of).ToList()) : result.Refusal();
	}

	private static async Task<IResult> BlocksAsync(HttpContext context, int id)
	{
		if (await context.ScopeAsync(id) is not { } scoped) return Api.NotFound("No such user.");

		var (result, list) = await scoped.Relationships.BlocksAsync(context.RequestAborted);
		return result.IsOk ? Api.Ok(list.Select(UserRef.Of).ToList()) : result.Refusal();
	}

	private static async Task<IResult> RelationAsync(HttpContext context, int id, int otherId,
		Func<ScopedUser, Task<Result<UserOutcome>>> act)
	{
		if (id != context.Connection().Record.Id)
			return Api.Error(StatusCodes.Status403Forbidden, "notSelf", "Only the account itself can do this.");

		return await context.ScopeAsync(otherId) is not { } scoped
			? Api.NotFound("No such user.")
			: (await act(scoped)).Answer();
	}

	private static async Task<IResult> RestrictAsync(HttpContext context, int id, RestrictRequest request)
	{
		if (await context.ScopeAsync(id) is not { } scoped) return Api.NotFound("No such user.");

		var (result, restriction) = await scoped.Restrictions.RestrictAsync((UserPermissions)request.Permissions,
			request.EndsAt, request.Reason, context.RequestAborted);
		return result.IsOk ? Api.Created(RestrictionView.Of(restriction!)) : result.Refusal();
	}

	private static async Task<IResult> LiftAsync(HttpContext context, int id, int rid, IRestrictionRepository repo)
	{
		if (await context.ScopeAsync(id) is not { } scoped) return Api.NotFound("No such user.");

		return await repo.GetAsync(rid, context.RequestAborted) is not { } restriction
			? Api.NotFound("No such restriction.")
			: (await scoped.Restrictions.LiftAsync(restriction, null, context.RequestAborted)).Answer();
	}

	private static async Task<IResult> DeleteRestrictionAsync(HttpContext context, int id, int rid,
		IRestrictionRepository repo)
	{
		if (await context.ScopeAsync(id) is not { } scoped) return Api.NotFound("No such user.");

		return await repo.GetAsync(rid, context.RequestAborted) is not { } restriction
			? Api.NotFound("No such restriction.")
			: (await scoped.Restrictions.DeleteAsync(restriction, null, context.RequestAborted)).Answer();
	}
}
