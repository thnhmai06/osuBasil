using Basil.Application.Sessions.Messages;
using Basil.Application.Users.Registry;
using Basil.Application.Users;
using System.Text;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Basil.Gateway.Limits;
using Basil.Host.Rest.Http;
using Basil.Host.Rest.Sse;
using Basil.Host.Shared.Views;
using Basil.Shared.Structures.Cryptography;
using Mediator;
using Session = Basil.Application.Sessions.Objects.Session;

namespace Basil.Host.Rest.Routes;

internal static class UserRoutes
{
	public static void MapUsers(this RouteGroupBuilder v1)
	{
		var users = v1.MapGroup("/users").WithTags("Users");
		var token = users.MapGroup("").RequireToken();

		users.MapGet("/{id:int}", GetAsync).WithSummary("Get an account.")
			.WithDescription(
				"Includes the granted and the effective privileges and the end of the silence in force. The profile and privileges of an account are public.");

		token.MapSpectate();
		token.MapGet("/", SearchAsync).Admitted(WorkClass.Listing).WithSummary("Search accounts.")
			.WithDescription("`query` uses the user search syntax.");
		token.MapPost("/", CreateAsync).WithSummary("Create an account.")
			.WithDescription("For staff only. Accounts do not sign themselves up through this API.");
		token.MapPatch("/{id:int}", PatchAsync).WithSummary("Update an account.")
			.WithDescription(
				"Only the fields sent change. `away` and `pmPrivate` can be changed by the account itself only, and an empty `away` clears the away message.");
		token.MapPut("/{id:int}/password", PasswordAsync).WithSummary("Update the password of an account.")
			.WithDescription("""
			                 For one's own account the current password is required and every other session is closed; with `includeCurrent` the calling session is closed too. Staff change another account's password without the current one, and every session of that account is closed.

			                 Signing an osu! client out only disconnects it, and it can sign in again with its password; changing the password is what keeps the client out. While the profiles area is locked, only staff can change a password.
			                 """);
		token.MapDelete("/{id:int}", DeleteAsync)
			.WithSummary("Delete an account.")
			.WithDescription("The record of the account is kept and every session of it ends.");
		token.MapPut("/{id:int}/avatar", PutAvatarAsync).Admitted(WorkClass.Upload).DisableAntiforgery()
			.WithSummary("Upload the avatar of an account.")
			.WithDescription("""
			                 The multipart form carries the picture in `file`: a PNG or JPEG within the configured size and pixel limits. A file over the size limit is refused with 422 and the error code `tooLarge`; content that is not such a picture, or exceeds a pixel limit, is refused with 422.

			                 While the profiles area is locked only staff can change an avatar.
			                 """);
		token.MapDelete("/{id:int}/avatar", DeleteAvatarAsync).WithSummary("Delete the avatar of an account.");

		token.MapGet("/{id:int}/sessions", ListSessionsAsync).Admitted(WorkClass.Listing)
			.WithSummary("List sessions of an account.")
			.WithDescription(
				"Filter by `state` (`all`, `online` or `offline`) and by `types`, a bitmask of the session types.");
		token.MapGet("/{id:int}/sessions/{sid:long}", GetSessionAsync)
			.WithSummary("Get a session of an account.");
		token.MapDelete("/{id:int}/sessions/{sid:long}", CloseSessionAsync)
			.WithSummary("Delete a session of an account.")
			.WithDescription(
				"Closing an osu! session only disconnects the client, which can sign in again with its password; change the password to keep the client out.");
		token.MapDelete("/{id:int}/sessions", CloseSessionsAsync).WithSummary("Delete every session of an account.")
			.WithDescription("The optional body `keepCurrent` (default `true`) keeps the calling session.");

		token.MapGet("/{id:int}/friends", FriendsAsync).Admitted(WorkClass.Listing)
			.WithSummary("List friends of an account.");
		token.MapPut("/{id:int}/friends/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, id, otherId, (me, other) => new AddFriend(me, other)))
			.WithSummary("Create a friend.")
			.WithDescription("Only the account itself can do this; any other caller is answered with 403.");
		token.MapDelete("/{id:int}/friends/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, id, otherId, (me, other) => new RemoveFriend(me, other)))
			.WithSummary("Delete a friend.")
			.WithDescription("Only the account itself can do this; any other caller is answered with 403.");
		token.MapGet("/{id:int}/blocks", BlocksAsync).Admitted(WorkClass.Listing)
			.WithSummary("List blocks of an account.");
		token.MapPut("/{id:int}/blocks/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, id, otherId, (me, other) => new BlockUser(me, other)))
			.WithSummary("Create a block.")
			.WithDescription("Only the account itself can do this; any other caller is answered with 403.");
		token.MapDelete("/{id:int}/blocks/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, id, otherId, (me, other) => new UnblockUser(me, other)))
			.WithSummary("Delete a block.")
			.WithDescription("Only the account itself can do this; any other caller is answered with 403.");

		token.MapPut("/{id:int}/silence", SilenceAsync)
			.WithSummary("Create a silence on an account.")
			.WithDescription(
				"The silence lasts until `endsAt`. A silenced account cannot post chat messages and receives no private messages.");
		token.MapDelete("/{id:int}/silence", UnsilenceAsync).WithSummary("Delete the silence of an account.")
			.WithDescription("The silence ends at once.");
	}

	private static async Task<IResult> GetAsync(int id, ISender sender, UserRegistry registry,
		TimeProvider clock, CancellationToken ct)
	{
		var record = await sender.Send(new GetUser(id), ct);
		return record is null
			? Api.NotFound("No such user.")
			: Api.Ok(UserView.Of(registry, record, clock));
	}

	private static async Task<IResult> SearchAsync(HttpContext context, string? query, int? offset, int? limit,
		ISender sender, UserRegistry registry, TimeProvider clock, CancellationToken ct)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var found = await sender.Send(new SearchUsers(context.Session(), UserQuery.Parse(query ?? string.Empty),
			page!), ct);
		return Api.Paged(
			new Page<UserView>([.. found.Items.Select(u => UserView.Of(registry, u, clock))], found.HasNext),
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

		var (record, result) = await context.Sender().Send(new CreateAccount(context.Session(), data,
			new Md5Hash(Encoding.UTF8.GetBytes(request.Password)), request.Privileges), context.RequestAborted);
		return result == UserActionResult.Ok ? Api.Created(UserView.Of(registry, record!, clock)) : result.Refusal();
	}

	private static async Task<IResult> PatchAsync(HttpContext context, int id, PatchUserRequest request,
		ISender sender, UserRegistry registry, TimeProvider clock)
	{
		var target = id;

		var ct = context.RequestAborted;
		if (await PatchNameAndCountryAsync(context, target, request, ct) is { } refusal) return refusal;
		if (await PatchSelfAndPrivilegesAsync(context, target, request, ct) is { } denied) return denied;

		var record = await sender.Send(new GetUser(target), ct);
		return record is null
			? Api.NotFound("No such user.")
			: Api.Ok(UserView.Of(registry, registry.Of(record).Record, clock), "Updated successfully");
	}

	private static async Task<IResult?> PatchNameAndCountryAsync(HttpContext context, int target,
		PatchUserRequest request, CancellationToken ct)
	{
		if (request.Name is not null &&
		    await context.Sender().Send(new RenameUser(context.Session(), target, request.Name), ct) is
			    var renamed and not UserActionResult.Ok)
			return renamed.Refusal();

		if (request.Country is null) return null;
		if (Targets.ParseCountry(request.Country) is not { } country)
			return Api.Invalid("country must be a two-letter code.");
		return await context.Sender().Send(new SetUserCountry(context.Session(), target, country), ct) is
			var changed and not UserActionResult.Ok
			? changed.Refusal()
			: null;
	}

	private static async Task<IResult?> PatchSelfAndPrivilegesAsync(HttpContext context, int target,
		PatchUserRequest request, CancellationToken ct)
	{
		if (request.Away is not null && await context.Sender().Send(
				    new SetUserAway(context.Session(), target, request.Away.Length == 0 ? null : request.Away), ct) is
			    var away and not UserActionResult.Ok)
			return NotSelf(away);
		if (request.PmPrivate is { } pm &&
		    await context.Sender().Send(new SetUserPmPrivate(context.Session(), target, pm), ct) is
			    var pmPrivate and not UserActionResult.Ok)
			return NotSelf(pmPrivate);
		if (request.Privileges is { } privileges &&
		    await context.Sender()
				    .Send(new SetUserPrivileges(context.Session(), target, privileges), ct) is
			    var set and not UserActionResult.Ok)
			return set.Refusal();

		return null;
	}

	private static IResult NotSelf(UserActionResult refused)
	{
		return refused == UserActionResult.Denied
			? Api.Error(StatusCodes.Status403Forbidden, "notSelf", "Only the account itself can do this.")
			: refused.Refusal();
	}

	private static async Task<IResult> PasswordAsync(HttpContext context, int id, PasswordRequest request)
	{
		if (string.IsNullOrEmpty(request.New)) return Api.Invalid("new is required.");
		var target = id;

		var current = request.Current is null ? (Md5Hash?)null : new Md5Hash(Encoding.UTF8.GetBytes(request.Current));
		return (await context.Sender().Send(new ChangeUserPassword(context.Session(), target,
				new Md5Hash(Encoding.UTF8.GetBytes(request.New)), current, request.IncludeCurrent ?? false),
			context.RequestAborted)).Answer();
	}

	private static async Task<IResult> DeleteAsync(HttpContext context, int id)
	{
		return (await context.Sender().Send(new DeleteUser(context.Session(), id),
			context.RequestAborted)).Answer();
	}

	private static async Task<IResult> PutAvatarAsync(HttpContext context, int id, ISender sender)
	{
		if (!context.Request.HasFormContentType) return Api.Invalid("A multipart form with a file field is required.");

		await UploadLimit.AllowAsync(context, sender);
		var form = await context.Request.ReadFormAsync(context.RequestAborted);
		if (form.Files.GetFile("file") is not { } file) return Api.Invalid("The file field is missing.");
		if (file.Length > (await sender.Send(new ReadSettings(), context.RequestAborted)).Uploads.AvatarMaxSize)
			return Api.Invalid("The avatar is too large.", "tooLarge");
		var target = id;

		await using var stream = file.OpenReadStream();
		return (await context.Sender()
			.Send(new SetUserAvatar(context.Session(), target, stream), context.RequestAborted)).Answer();
	}

	private static async Task<IResult> DeleteAvatarAsync(HttpContext context, int id)
	{
		return (await context.Sender().Send(new RemoveUserAvatar(context.Session(), id),
			context.RequestAborted)).Answer();
	}

	private static async Task<IResult> ListSessionsAsync(HttpContext context, int id, string? state, int? types,
		int? offset, int? limit)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var sessionState = (state ?? "all").ToLowerInvariant() switch
		{
			"all" => (SessionFilter?)SessionFilter.Any,
			"online" => SessionFilter.Active,
			"offline" => SessionFilter.Ended,
			_ => null
		};
		if (sessionState is null) return Api.Invalid("state must be all, online or offline.");

		var kinds = types is null
			? null
			: Enum.GetValues<SessionType>().Where(t => (types.Value & (int)t) != 0).ToList();
		var target = id;

		var (result, sessions) =
			await context.Sender().Send(
				new ListUserSessions(context.Session(), target, page!, sessionState.Value, kinds),
				context.RequestAborted);
		if (result != UserActionResult.Ok) return result.Refusal();

		return Api.Paged(new Page<SessionView>([.. sessions!.Items.Select(SessionView.Of)], sessions.HasNext), page!);
	}

	private static async Task<IResult> GetSessionAsync(HttpContext context, int id, long sid)
	{
		var target = id;

		var (result, session) = await context.Sender()
			.Send(new GetUserSession(context.Session(), target, sid), context.RequestAborted);
		return result == UserActionResult.Ok ? Api.Ok(SessionView.Of(session!)) : result.Refusal();
	}

	private static async Task<IResult> CloseSessionAsync(HttpContext context, int id, long sid)
	{
		return (await context.Sender().Send(new CloseUserSession(context.Session(), id, sid),
			context.RequestAborted)).Answer();
	}

	private static async Task<IResult> CloseSessionsAsync(HttpContext context, int id)
	{
		var (request, refusal) = await context.OptionalBodyAsync<CloseSessionsRequest>();
		if (refusal is not null) return refusal;

		return (await context.Sender().Send(
			new CloseUserSessions(context.Session(), id, request?.KeepCurrent ?? true),
			context.RequestAborted)).Answer();
	}

	private static async Task<IResult> FriendsAsync(HttpContext context, int id)
	{
		var target = id;

		var (result, list) =
			await context.Sender().Send(new ListFriends(context.Session(), target), context.RequestAborted);
		return result == UserActionResult.Ok ? Api.Ok(list.Select(UserRef.Of).ToList()) : result.Refusal();
	}

	private static async Task<IResult> BlocksAsync(HttpContext context, int id)
	{
		var target = id;

		var (result, list) =
			await context.Sender().Send(new ListBlocks(context.Session(), target), context.RequestAborted);
		return result == UserActionResult.Ok ? Api.Ok(list.Select(UserRef.Of).ToList()) : result.Refusal();
	}

	private static async Task<IResult> RelationAsync(HttpContext context, int id, int otherId,
		Func<Session, int, IRequest<UserActionResult>> ask)
	{
		if (id != context.Session().Record.Id)
			return Api.Error(StatusCodes.Status403Forbidden, "notSelf", "Only the account itself can do this.");

		return (await context.Sender().Send(ask(context.Session(), otherId),
			context.RequestAborted)).Answer();
	}

	private static async Task<IResult> SilenceAsync(HttpContext context, int id, SilenceRequest request)
	{
		var target = id;

		return (await context.Sender().Send(new SilenceUser(context.Session(), target, request.EndsAt,
			request.Reason), context.RequestAborted)).Answer();
	}

	private static async Task<IResult> UnsilenceAsync(HttpContext context, int id)
	{
		var target = id;

		return (await context.Sender().Send(new UnsilenceUser(context.Session(), target, null),
			context.RequestAborted)).Answer();
	}

	public sealed record PatchUserRequest(
		string? Name,
		string? Country,
		string? Away,
		bool? PmPrivate,
		UserPrivileges? Privileges);

	public sealed record PasswordRequest(string? Current, string? New, bool? IncludeCurrent);

	public sealed record CreateUserRequest(
		string? Username,
		string? Password,
		string? Country,
		UserPrivileges? Privileges);

	public sealed record CloseSessionsRequest(bool? KeepCurrent);

	public sealed record SilenceRequest(DateTimeOffset EndsAt, string? Reason);
}