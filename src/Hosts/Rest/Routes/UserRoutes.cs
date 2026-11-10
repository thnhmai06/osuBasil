using System.Text;
using System.Text.Json;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Gateway;
using Basil.Host.Rest.Http;
using Basil.Host.Rest.Sse;
using Basil.Host.Shared.Views;
using Mediator;

namespace Basil.Host.Rest.Routes;

/// <summary>Accounts, their sessions, friends, blocks and silences.</summary>
internal static class UserRoutes
{
	public static void MapUsers(this RouteGroupBuilder v1)
	{
		var users = v1.MapGroup("/users").WithTags("Users");
		var token = users.MapGroup("").RequireToken();

		users.MapGet("/{id:int}", GetAsync).WithSummary("Get an account.")
			.WithDescription(
				"Includes the granted and the effective privileges and the end of the silence in force. An account's profile and privileges are public.");

		token.MapSpectate();
		token.MapGet("/", SearchAsync).Admitted(WorkClass.Listing).WithSummary("Search accounts.")
			.WithDescription("`query` uses the user search syntax. Paged with `offset` and `limit`.");
		token.MapPost("/", CreateAsync).WithSummary("Create an account.")
			.WithDescription("For staff only. Accounts are not created by themselves through this API.");
		token.MapPatch("/{id:int}", PatchAsync).WithSummary("Update an account.");
		token.MapPut("/{id:int}/password", PasswordAsync).WithSummary("Update the password of an account.")
			.WithDescription("""
			                 For one's own account the current password is required and every other session is closed; with
			                 `includeCurrent` the calling session is closed too. Staff change another account's password without
			                 the current one and every session of that account is closed.

			                 Signing an osu! client out only disconnects it, and it can sign in again with its password; changing
			                 the password is what keeps the client out. While the profiles area is locked, only staff can change a
			                 password.
			                 """);
		token.MapDelete("/{id:int}", DeleteAsync)
			.WithSummary("Delete an account, keeping its record but ending every session.");
		token.MapPut("/{id:int}/avatar", PutAvatarAsync).Admitted(WorkClass.Upload).DisableAntiforgery()
			.WithSummary("Upload the avatar of an account as an image in the `file` field.")
			.WithDescription("""
			                 While the profiles area is locked only staff can change an avatar. The avatar is shown on the avatar host.

			                 The file must be a PNG or JPEG picture within the configured size and pixel limits. Content that is not
			                 such a picture, or that exceeds a limit, is refused with `422`.
			                 """);
		token.MapDelete("/{id:int}/avatar", DeleteAvatarAsync).WithSummary("Delete the avatar of an account.");

		token.MapGet("/{id:int}/sessions", ListSessionsAsync).Admitted(WorkClass.Listing)
			.WithSummary("List the sessions of an account.")
			.WithDescription(
				"Filter by `state` (all, online, offline) and by `types`, a bitmask of the connection kinds.");
		token.MapGet("/{id:int}/sessions/{sid:long}", GetSessionAsync)
			.WithSummary("Get one session of an account.");
		token.MapGet("/{id:int}/sessions/{sid:long}/connections", ListConnectionsAsync).Admitted(WorkClass.Listing)
			.WithSummary("List the connections of one session of an account.")
			.WithDescription(
				"Each connection tells when and from which address a client connected and how the connection ended, newest first. A client that comes back from the same address soon after it left continues its last connection. Paged with `offset` and `limit`.");
		token.MapDelete("/{id:int}/sessions/{sid:long}", CloseSessionAsync)
			.WithSummary("Delete one session of an account.")
			.WithDescription(
				"Closing an osu! session only disconnects the client, which can sign in again with its password; change the password to keep the client out.");
		token.MapDelete("/{id:int}/sessions", CloseSessionsAsync).WithSummary("Delete every session of an account.");
		token.MapPost("/{id:int}/sessions/irc", OpenIrcAsync).Accepts<IrcKeyRequest>(true, "application/json", "*/*")
			.WithSummary("Create an IRC key for one device.")
			.WithDescription("The key is shown only in this response. Close the key's session to revoke it.");

		token.MapGet("/{id:int}/friends", FriendsAsync).Admitted(WorkClass.Listing)
			.WithSummary("List the friends of the calling account.");
		token.MapPut("/{id:int}/friends/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, id, otherId, (me, other) => new AddFriend(me, other)))
			.WithSummary("Create a friend.");
		token.MapDelete("/{id:int}/friends/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, id, otherId, (me, other) => new RemoveFriend(me, other)))
			.WithSummary("Delete a friend.");
		token.MapGet("/{id:int}/blocks", BlocksAsync).Admitted(WorkClass.Listing)
			.WithSummary("List the accounts the calling account blocked.");
		token.MapPut("/{id:int}/blocks/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, id, otherId, (me, other) => new BlockUser(me, other)))
			.WithSummary("Create a block on an account.");
		token.MapDelete("/{id:int}/blocks/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, id, otherId, (me, other) => new UnblockUser(me, other)))
			.WithSummary("Delete a block on an account.");

		token.MapPut("/{id:int}/silence", SilenceAsync).WithSummary("Create a silence on an account until a given time.")
			.WithDescription("A silenced account cannot post chat messages and receives no private messages.");
		token.MapDelete("/{id:int}/silence", UnsilenceAsync).WithSummary("Delete the silence of an account now.");
	}

	private static async Task<IResult> GetAsync(int id, ISender sender, UserRegistry registry,
		TimeProvider clock, CancellationToken ct)
	{
		var record = await sender.Send(new GetUser(UserReference.ById(id)), ct);
		return record is null
			? Api.NotFound("No such user.")
			: Api.Ok(UserView.Of(registry, record, clock));
	}

	private static async Task<IResult> SearchAsync(HttpContext context, string? query, int? offset, int? limit,
		ISender sender, UserRegistry registry, TimeProvider clock, CancellationToken ct)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var found = await sender.Send(new SearchUsers(context.Connection(), UserQuery.Parse(query ?? string.Empty),
			page!), ct);
		return Api.Paged(new Page<UserView>([.. found.Items.Select(u => UserView.Of(registry, u, clock))], found.HasNext),
			page!);
	}

	private static async Task<IResult> CreateAsync(HttpContext context, CreateUserRequest request,
		UserRegistry registry,
		TimeProvider clock)
	{
		if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
			return Api.Invalid("username and password are required.");

		var data = new UserData { Name = request.Username };
				if (Targets.ParseCountry(request.Country) is { } country) data.Country = country;

		var (record, result) = await context.Sender().Send(new CreateAccount(context.Connection(), data,
			new Md5(Encoding.UTF8.GetBytes(request.Password)), request.Privileges), context.RequestAborted);
		return result.IsOk ? Api.Created(UserView.Of(registry, record!, clock)) : result.Refusal();
	}

	private static async Task<IResult> PatchAsync(HttpContext context, int id, PatchUserRequest request,
		ISender sender, UserRegistry registry, TimeProvider clock)
	{
		var target = UserReference.ById(id);

		var ct = context.RequestAborted;
		if (await PatchNameAndCountryAsync(context, target, request, ct) is { } refusal) return refusal;
		if (await PatchSelfAndPrivilegesAsync(context, target, request, ct) is { } denied) return denied;

		var record = await sender.Send(new GetUser(target), ct);
		return record is null
			? Api.NotFound("No such user.")
			: Api.Ok(UserView.Of(registry, registry.Of(record).Record, clock), "Updated successfully");
	}

	private static async Task<IResult?> PatchNameAndCountryAsync(HttpContext context, UserReference target,
		PatchUserRequest request, CancellationToken ct)
	{
		if (request.Name is not null &&
		    await context.Sender().Send(new RenameUser(context.Connection(), target, request.Name), ct) is
			    { IsOk: false } renamed)
			return renamed.Refusal();

		if (request.Country is null) return null;
		if (Targets.ParseCountry(request.Country) is not { } country)
			return Api.Invalid("country must be a two-letter code.");
		return await context.Sender().Send(new SetUserCountry(context.Connection(), target, country), ct) is
			{ IsOk: false } changed
			? changed.Refusal()
			: null;
	}

	private static async Task<IResult?> PatchSelfAndPrivilegesAsync(HttpContext context, UserReference target,
		PatchUserRequest request, CancellationToken ct)
	{
		if (request.Away is not null && !await context.Sender().Send(
			    new SetUserAway(context.Connection(), target, request.Away.Length == 0 ? null : request.Away), ct))
			return Api.Error(StatusCodes.Status403Forbidden, "notSelf", "Only the account itself can do this.");
		if (request.PmPrivate is { } pm &&
		    !await context.Sender().Send(new SetUserPmPrivate(context.Connection(), target, pm), ct))
			return Api.Error(StatusCodes.Status403Forbidden, "notSelf", "Only the account itself can do this.");
		if (request.Privileges is { } privileges &&
		    await context.Sender()
				    .Send(new SetUserPrivileges(context.Connection(), target, privileges), ct) is
			    { IsOk: false } set)
			return set.Refusal();

		return null;
	}

	private static async Task<IResult> PasswordAsync(HttpContext context, int id, PasswordRequest request)
	{
		if (string.IsNullOrEmpty(request.New)) return Api.Invalid("new is required.");
		var target = UserReference.ById(id);

		var current = request.Current is null ? (Md5?)null : new Md5(Encoding.UTF8.GetBytes(request.Current));
		return (await context.Sender().Send(new ChangeUserPassword(context.Connection(), target,
				new Md5(Encoding.UTF8.GetBytes(request.New)), current, request.IncludeCurrent ?? false),
			context.RequestAborted)).Answer();
	}

	private static async Task<IResult> DeleteAsync(HttpContext context, int id)
	{
		return (await context.Sender().Send(new DeleteUser(context.Connection(), UserReference.ById(id)),
			context.RequestAborted)).Answer();
	}

	private static async Task<IResult> PutAvatarAsync(HttpContext context, int id, ISettingsRepository settings)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form with a file field is required.");

		await UploadLimit.AllowAsync(context, settings);
		var form = await context.Request.ReadFormAsync(context.RequestAborted);
		if (form.Files.GetFile("file") is not { } file) return Api.Invalid("The file field is missing.");
		if (file.Length > (await settings.GetAsync(context.RequestAborted)).Uploads.AvatarMaxSize)
			return Api.Invalid("The avatar is too large.", "tooLarge");
		var target = UserReference.ById(id);

		await using var stream = file.OpenReadStream();
		return (await context.Sender()
			.Send(new SetUserAvatar(context.Connection(), target, stream), context.RequestAborted)).Answer();
	}

	private static async Task<IResult> DeleteAvatarAsync(HttpContext context, int id)
	{
		return (await context.Sender().Send(new RemoveUserAvatar(context.Connection(), UserReference.ById(id)),
			context.RequestAborted)).Answer();
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
		var target = UserReference.ById(id);

		var (result, sessions) =
			await context.Sender().Send(
				new ListUserSessions(context.Connection(), target, page!, sessionState.Value, kinds),
				context.RequestAborted);
		if (!result.IsOk) return result.Refusal();

		return Api.Paged(new Page<SessionView>([.. sessions!.Items.Select(SessionView.Of)], sessions.HasNext), page!);
	}

	private static async Task<IResult> GetSessionAsync(HttpContext context, int id, long sid)
	{
		var target = UserReference.ById(id);

		var (result, session) = await context.Sender()
			.Send(new GetUserSession(context.Connection(), target, sid), context.RequestAborted);
		return result.IsOk ? Api.Ok(SessionView.Of(session!)) : result.Refusal();
	}

	private static async Task<IResult> ListConnectionsAsync(HttpContext context, int id, long sid, int? offset,
		int? limit)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var target = UserReference.ById(id);

		var (result, connections) = await context.Sender()
			.Send(new ListSessionConnections(context.Connection(), target, sid, page!), context.RequestAborted);
		return result.IsOk
			? Api.Paged(new Page<ConnectionView>([.. connections!.Items.Select(ConnectionView.Of)], connections.HasNext),
				page!)
			: result.Refusal();
	}

	private static async Task<IResult> CloseSessionAsync(HttpContext context, int id, long sid)
	{
		return (await context.Sender().Send(new CloseUserSession(context.Connection(), UserReference.ById(id), sid),
			context.RequestAborted)).Answer();
	}

	private static async Task<IResult> CloseSessionsAsync(HttpContext context, int id)
	{
		var (request, refusal) = await context.OptionalBodyAsync<CloseSessionsRequest>();
		if (refusal is not null) return refusal;

		return (await context.Sender().Send(
			new CloseUserSessions(context.Connection(), UserReference.ById(id), request?.KeepCurrent ?? true),
			context.RequestAborted)).Answer();
	}

	private static async Task<IResult> OpenIrcAsync(HttpContext context, int id)
	{
		if (!context.Request.HasJsonContentType()) return Api.Invalid("A JSON body is required.");
		IrcKeyRequest? request;
		try
		{
			request = await context.Request.ReadFromJsonAsync<IrcKeyRequest>(Api.Json, context.RequestAborted);
		}
		catch (JsonException)
		{
			return Api.Invalid("The body is not valid.");
		}

		if (request is null) return Api.Invalid("A body is required.");
		if (string.IsNullOrWhiteSpace(request.Label)) return Api.Invalid("label is required.");
		var target = UserReference.ById(id);

		var (result, key, session) = await context.Sender()
			.Send(new OpenIrcSession(context.Connection(), target, request.Label), context.RequestAborted);
		return result.IsOk ? Api.Created(new IrcKeyResponse(session!.Id, key!)) : result.Refusal();
	}

	private static async Task<IResult> FriendsAsync(HttpContext context, int id)
	{
		var target = UserReference.ById(id);

		var (result, list) =
			await context.Sender().Send(new ListFriends(context.Connection(), target), context.RequestAborted);
		return result.IsOk ? Api.Ok(list.Select(UserRef.Of).ToList()) : result.Refusal();
	}

	private static async Task<IResult> BlocksAsync(HttpContext context, int id)
	{
		var target = UserReference.ById(id);

		var (result, list) =
			await context.Sender().Send(new ListBlocks(context.Connection(), target), context.RequestAborted);
		return result.IsOk ? Api.Ok(list.Select(UserRef.Of).ToList()) : result.Refusal();
	}

	private static async Task<IResult> RelationAsync(HttpContext context, int id, int otherId,
		Func<Connection, UserReference, IRequest<Result<UserOutcome>>> ask)
	{
		if (id != context.Connection().Record.Id)
			return Api.Error(StatusCodes.Status403Forbidden, "notSelf", "Only the account itself can do this.");

		return (await context.Sender().Send(ask(context.Connection(), UserReference.ById(otherId)),
			context.RequestAborted)).Answer();
	}

	private static async Task<IResult> SilenceAsync(HttpContext context, int id, SilenceRequest request)
	{
		var target = UserReference.ById(id);

		return (await context.Sender().Send(new SilenceUser(context.Connection(), target, request.EndsAt,
			request.Reason), context.RequestAborted)).Answer();
	}

	private static async Task<IResult> UnsilenceAsync(HttpContext context, int id)
	{
		var target = UserReference.ById(id);

		return (await context.Sender().Send(new UnsilenceUser(context.Connection(), target, null),
			context.RequestAborted)).Answer();
	}

	/// <summary>The fields of an account that can change; absent fields stay as they are.</summary>
	public sealed record PatchUserRequest(
		string? Name,
		string? Country,
		string? Away,
		bool? PmPrivate,
		UserPrivileges? Privileges);

	/// <summary>A password change: the current password is needed for one's own account only.</summary>
	public sealed record PasswordRequest(string? Current, string? New, bool? IncludeCurrent);

	/// <summary>A new account made by staff.</summary>
	public sealed record CreateUserRequest(string? Username, string? Password, string? Country, UserPrivileges? Privileges);

	/// <summary>A request to close all sessions of one's own account.</summary>
	public sealed record CloseSessionsRequest(bool? KeepCurrent);

	/// <summary>A request to open an IRC key.</summary>
	public sealed record IrcKeyRequest(string? Label);

	/// <summary>A new IRC key; the key is shown only here.</summary>
	public sealed record IrcKeyResponse(long SessionId, string Key);

	/// <summary>A silence.</summary>
	public sealed record SilenceRequest(DateTimeOffset EndsAt, string? Reason);
}