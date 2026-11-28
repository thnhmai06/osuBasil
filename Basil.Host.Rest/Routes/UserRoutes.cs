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
using Basil.Host.Shared.Http;
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

		users.MapGet("/{id:int}", GetAsync).Returns<UserView>().WithSummary("Get an account.")
			.WithDescription(
				"Includes the granted and the effective privileges and the end of the silence in force. The profile and privileges of an account are public.");

		token.MapSpectate();
		token.MapGet("/", SearchAsync).Admitted(WorkClass.Listing).ReturnsList<UserView>().WithSummary("Search accounts.")
			.WithDescription("`query` uses the user search syntax.");
		token.MapPost("/", CreateAsync).Takes<CreateUserRequest>().Returns<UserView>(201).WithSummary("Create an account.")
			.WithDescription("Needs the Owner privilege, and the new account can only be given privileges the caller holds. Accounts do not sign themselves up through this API.");
		token.MapPatch("/{id:int}", PatchAsync).Takes<PatchUserRequest>().Returns<UserView>().WithSummary("Update an account.")
			.WithDescription(
				"Only the fields sent change. `away` and `pmPrivate` can be changed by the account itself only, and an empty `away` clears the away message.");
		token.MapPut("/{id:int}/password", PasswordAsync).Takes<PasswordRequest>().WithSummary("Update the password of an account.")
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
			                 The multipart form carries the picture in `file`: a PNG or JPEG within the configured size and pixel limits. A file over the size limit is refused with 413 and the error code `tooLarge` when its size is declared, and by closing the connection otherwise; content that is not a PNG, JPEG, GIF or WebP picture closes the connection, and a picture that is unreadable or exceeds a pixel limit is refused with 422.

			                 While the profiles area is locked only staff can change an avatar.
			                 """);
		token.MapDelete("/{id:int}/avatar", DeleteAvatarAsync).WithSummary("Delete the avatar of an account.")
			.WithDescription("The account falls back to the default picture. Only the account itself, or the Owner privilege for an account of lower rank, can do this; while the profiles area is locked only the Owner can.");

		token.MapGet("/{id:int}/sessions", ListSessionsAsync).Admitted(WorkClass.Listing)
			.ReturnsList<SessionView>().WithSummary("List sessions of an account.")
			.WithDescription(
				"Filter by `state` (`all`, `online` or `offline`) and by `types`, a bitmask of the session types.");
		token.MapGet("/{id:int}/sessions/{sid:long}", GetSessionAsync)
			.Returns<SessionView>().WithSummary("Get a session of an account.")
			.WithDescription("Only the account itself, or a caller with the Moderator or Owner privilege, can read it. A session that does not exist for the account is answered with 404.");
		token.MapDelete("/{id:int}/sessions/{sid:long}", CloseSessionAsync)
			.WithSummary("Delete a session of an account.")
			.WithDescription(
				"Closing an osu! session only disconnects the client, which can sign in again with its password; change the password to keep the client out.");
		token.MapDelete("/{id:int}/sessions", CloseSessionsAsync).Takes<CloseSessionsRequest>().WithSummary("Delete every session of an account.")
			.WithDescription("The optional body `keepCurrent` (default `true`) keeps the calling session.");

		token.MapGet("/{id:int}/friends", FriendsAsync).Admitted(WorkClass.Listing)
			.ReturnsList<UserRef>().WithSummary("List friends of an account.")
			.WithDescription("Only the account itself can read the list; any other caller is answered with 403.");
		token.MapPut("/{id:int}/friends/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, new AddFriend(c.Session(), id, otherId)))
			.WithSummary("Create a friend.")
			.WithDescription("Only the account itself can do this; any other caller is answered with 403.");
		token.MapDelete("/{id:int}/friends/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, new RemoveFriend(c.Session(), id, otherId)))
			.WithSummary("Delete a friend.")
			.WithDescription("Only the account itself can do this; any other caller is answered with 403.");
		token.MapGet("/{id:int}/blocks", BlocksAsync).Admitted(WorkClass.Listing)
			.ReturnsList<UserRef>().WithSummary("List blocks of an account.")
			.WithDescription("Only the account itself can read the list; any other caller is answered with 403.");
		token.MapPut("/{id:int}/blocks/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, new BlockUser(c.Session(), id, otherId)))
			.WithSummary("Create a block.")
			.WithDescription("Only the account itself can do this; any other caller is answered with 403.");
		token.MapDelete("/{id:int}/blocks/{otherId:int}", (HttpContext c, int id, int otherId) =>
				RelationAsync(c, new UnblockUser(c.Session(), id, otherId)))
			.WithSummary("Delete a block.")
			.WithDescription("Only the account itself can do this; any other caller is answered with 403.");

		token.MapPut("/{id:int}/silence", SilenceAsync)
			.Takes<SilenceRequest>().WithSummary("Create a silence on an account.")
			.WithDescription(
				"The silence lasts until `endsAt`, which must be in the future. A silenced account cannot post chat messages and receives no private messages.");
		token.MapDelete("/{id:int}/silence", UnsilenceAsync).WithSummary("Delete the silence of an account.")
			.WithDescription("The silence ends at once. An account that is not silenced is answered with 404.");
	}

	private static async Task<IResult> GetAsync(int id, ISender sender, CancellationToken ct)
	{
		var record = await sender.Send(new GetUser(id), ct);
		return record is null
			? Api.NotFound("No such user.")
			: Api.Ok((await UserView.OfAsync(sender, [record], ct))[0]);
	}

	private static async Task<IResult> SearchAsync(HttpContext context, string? query, int? offset, int? limit,
		ISender sender, CancellationToken ct)
	{
		var (page, error) = Targets.Paging(offset, limit);
		if (error is not null) return error;

		var found = await sender.Send(new SearchUsers(context.Session(), UserQuery.Parse(query ?? string.Empty),
			page!), ct);
		return Api.Paged(
			new Page<UserView>(await UserView.OfAsync(sender, found.Items, ct), found.HasNext),
			page!);
	}

	private static async Task<IResult> CreateAsync(HttpContext context, ISender sender)
	{
		if (await context.Sender().Send(new MayCreateAccount(context.Session()), context.RequestAborted) is
		    var may and not UserActionResult.Ok)
			return may.Refusal();

		var (request, refused) = await context.ReadBodyAsync<CreateUserRequest>();
		if (refused is not null) return refused;

		if (string.IsNullOrWhiteSpace(request!.Username) || string.IsNullOrEmpty(request.Password))
			return Api.Invalid("username and password are required.");

		var data = new UserData { Name = request.Username };
		if (Targets.ParseCountry(request.Country) is { } country) data.Country = country;

		var (record, result) = await context.Sender().Send(new CreateAccount(context.Session(), data,
			new Md5Hash(Encoding.UTF8.GetBytes(request.Password)), request.Privileges), context.RequestAborted);
		return result == UserActionResult.Ok
			? Api.Created((await UserView.OfAsync(sender, [record!], context.RequestAborted))[0])
			: result.Refusal();
	}

	private static async Task<IResult> PatchAsync(HttpContext context, int id, ISender sender)
	{
		var (request, refused) = await context.ReadBodyAsync<PatchUserRequest>();
		if (refused is not null) return refused;

		if (request is { Name: null, Country: null, Away: null, PmPrivate: null, Privileges: null })
			return Api.Invalid("Send at least one field to change.");

		var country = Targets.ParseCountry(request!.Country);
		if (request.Country is not null && country is null) return Api.Invalid("country must be a two-letter code.");

		var ct = context.RequestAborted;
		var result = await sender.Send(new UpdateUser(context.Session(), id,
			new UserChange(request.Name, country, request.Away, request.PmPrivate, request.Privileges)), ct);
		if (result == UserActionResult.NotSelf)
			return Api.Error(StatusCodes.Status403Forbidden, "notSelf", "Only the account itself can do this.");
		if (result != UserActionResult.Ok) return result.Refusal();

		var record = await sender.Send(new GetUser(id), ct);
		return record is null
			? Api.NotFound("No such user.")
			: Api.Ok((await UserView.OfAsync(sender, [record], ct))[0], "Updated successfully");
	}

	private static async Task<IResult> PasswordAsync(HttpContext context, int id)
	{
		if (await context.Sender().Send(new MayChangeUserPassword(context.Session(), id), context.RequestAborted) is
		    var may and not UserActionResult.Ok)
			return may.Refusal();

		var (request, refused) = await context.ReadBodyAsync<PasswordRequest>();
		if (refused is not null) return refused;

		if (string.IsNullOrEmpty(request!.New)) return Api.Invalid("new is required.");
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

		var ct = context.RequestAborted;
		var session = context.Session();
		var limit = (await sender.Send(new ReadSettings(), ct)).Uploads.AvatarMaxSize;
		if (!UploadRequest.Accept(context, limit))
			return Api.Error(StatusCodes.Status413PayloadTooLarge, "tooLarge", "The avatar is too large.");

		var early = await sender.Send(new MaySetUserAvatar(session, id), ct);
		if (early != UserActionResult.Ok) return early.Refusal();

		return await UploadRequest.GuardAsync(context, "PUT /v1/users/{id}/avatar", $"user {session.Record.Id}",
			async () =>
			{
				await using var form = await UploadForm.ReadAsync(context, "file", UploadSignature.Image, limit, [],
					false, ct);
				if (form.File is null) return Api.Invalid("The file field is missing.");

				return (await sender.Send(new SetUserAvatar(session, id, form.File), ct)).Answer();
			});
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

	private static async Task<IResult> RelationAsync(HttpContext context, IRequest<UserActionResult> ask)
	{
		return (await context.Sender().Send(ask, context.RequestAborted)).Answer();
	}

	private static async Task<IResult> SilenceAsync(HttpContext context, int id)
	{
		var target = id;
		if (await context.Sender().Send(new MaySilenceUser(context.Session(), target), context.RequestAborted) is
		    var may and not UserActionResult.Ok)
			return may.Refusal();

		var (request, refused) = await context.ReadBodyAsync<SilenceRequest>();
		if (refused is not null) return refused;

		return (await context.Sender().Send(new SilenceUser(context.Session(), target, request!.EndsAt,
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