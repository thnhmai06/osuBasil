using System.Globalization;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Host.Shared.Views;
using Mediator;

namespace Basil.Host.Cli.Commands;

/// <summary>The <c>users</c> commands: accounts, their avatars, friends, blocks, silences and sessions.</summary>
internal static class UserCommands
{
	private const string NoUser = "user";

	public static IEnumerable<CommandDef> Create()
	{
		yield return new CommandDef
		{
			Pattern = "users list", Route = "GET /v1/users", Offline = true, Summary = "Searches accounts.",
			Options = [new OptionDef("query", "The user search syntax."), .. CommandHelpers.Paging], Run = ListAsync
		};
		yield return new CommandDef
		{
			Pattern = "users create", Route = "POST /v1/users", Offline = true, Summary = "Creates an account.",
			Options =
			[
				new OptionDef("username", "The name of the account.", Required: true, Alias: "u"),
				new OptionDef("password", "The plain password.", Required: true, Alias: "p", Secret: true),
				new OptionDef("country", "A two-letter country code."),
				new OptionDef("privileges", "The privileges, as a bitmask (default: the default privileges).",
					OptionKind.Mask),
				new OptionDef("admin", "Give the account every privilege.", OptionKind.Bool)
			],
			Run = CreateAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int>", Route = "GET /v1/users/{id}", Offline = true, Summary = "Shows an account.",
			Run = ShowAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> set", Route = "PATCH /v1/users/{id}", Routes = ["PUT /v1/users/{id}/password"],
			Offline = true, Summary = "Changes the given fields of an account.",
			Options =
			[
				new OptionDef("username", "The new name."), new OptionDef("country", "A two-letter country code."),
				new OptionDef("away", "The away message; empty clears it."),
				new OptionDef("pmPrivate", "Block private messages from strangers.", OptionKind.Bool),
				new OptionDef("privileges", "The granted privileges, as a bitmask.", OptionKind.Mask),
				new OptionDef("password", "A new plain password; every session of the account is closed.", Secret: true)
			],
			Run = SetAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> avatar", Route = "PUT /v1/users/{id}/avatar", Offline = true,
			Summary = "Sets the avatar from an image file.",
			Options = [new OptionDef("file", "The path of the image.", OptionKind.Path, true)], Run = SetAvatarAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> avatar delete", Route = "DELETE /v1/users/{id}/avatar", Offline = true,
			Summary = "Removes the avatar.", Run = RemoveAvatarAsync
		};
		foreach (var (word, type) in new[] { ("friends", RelationshipType.Friend), ("blocks", RelationshipType.Block) })
		{
			var kind = type;
			yield return new CommandDef
			{
				Pattern = $"users <id:int> {word} list", Route = $"GET /v1/users/{{id}}/{word}", Offline = true,
				Summary =
					$"Lists the accounts this account has {(kind == RelationshipType.Friend ? "as friends" : "blocked")}.",
				Run = call => RelationsAsync(call, kind)
			};
		}

		yield return new CommandDef
		{
			Pattern = "users <id:int> silence", Route = "PUT /v1/users/{id}/silence", Offline = true,
			Summary = "Silences an account: it cannot post chat messages.",
			Options =
			[
				new OptionDef("duration", "How long it lasts, such as 30m, 12h or 7d.", OptionKind.Duration, true),
				new OptionDef("reason", "Why.")
			],
			Run = SilenceAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> unsilence", Route = "DELETE /v1/users/{id}/silence", Offline = true,
			Summary = "Ends the silence of an account now.", Run = UnsilenceAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> sessions list", Route = "GET /v1/users/{id}/sessions", Offline = true,
			Summary = "Lists the sessions of an account.",
			Options =
			[
				new OptionDef("state", "all, online or offline (default all)."),
				new OptionDef("types", "The connection kinds, as a bitmask.", OptionKind.Mask), .. CommandHelpers.Paging
			],
			Run = SessionsAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> sessions <sid:long>", Route = "GET /v1/users/{id}/sessions/{sid}", Offline = true,
			Summary = "Shows one session.", Run = SessionAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> sessions <sid:long> connections",
			Route = "GET /v1/users/{id}/sessions/{sid}/connections", Offline = true,
			Summary = "Lists the connections of one session.", Options = [.. CommandHelpers.Paging],
			Run = SessionConnectionsAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> sessions <sid:long> delete", Route = "DELETE /v1/users/{id}/sessions/{sid}",
			Offline = true, Summary = "Closes one session.", Run = CloseSessionAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> sessions delete", Route = "DELETE /v1/users/{id}/sessions", Offline = true,
			Summary = "Closes every session of an account.", Run = CloseSessionsAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> delete", Route = "DELETE /v1/users/{id}", Offline = true,
			Summary = "Deletes an account for good.", Run = DeleteAsync
		};
	}

	private static async Task<int> ListAsync(CommandCall call)
	{
		var page = call.Page();
		var registry = call.Get<UserRegistry>();
		var clock = call.Get<TimeProvider>();
		var found = await call.Get<IUserRepository>().ListAsync(UserQuery.Parse(call.Text("query") ?? string.Empty),
			page,
			call.CancellationToken);
		call.PrintPage(new Page<UserView>(
			[.. found.Items.Select(user => UserView.Of(registry, user, clock))], found.HasNext), page);
		return ExitCodes.Success;
	}

	private static async Task<int> CreateAsync(CommandCall call)
	{
		var data = new UserData
		{
			Name = call.Required("username")
		};
		UserPrivileges? privileges = call.Bool("admin") == true
			? UserPrivilegesExtensions.All
			: call.Mask("privileges") is { } mask
				? (UserPrivileges)mask
				: null;
		if (call.Text("country") is { } code)
			data.Country = Enum.TryParse<Country>(code, true, out var country) && code.Length == 2 &&
			               Enum.IsDefined(country)
				? country
				: throw CommandException.Invalid("--country must be a two-letter code.");

		var actor = await call.ByAsync();
		var (record, result) = await call.Get<ISender>().Send(new CreateAccount(actor, data,
			CommandHelpers.Md5Of(call.Required("password")), privileges), call.CancellationToken);
		return result.IsOk
			? Created(call, record!)
			: call.Refuse(result);
	}

	private static int Created(CommandCall call, UserRecord record)
	{
		call.Output.Ok($"Created {record.Value.Name} with id {record.Id}.");
		return ExitCodes.Success;
	}

	private static async Task<int> ShowAsync(CommandCall call)
	{
		if (await call.FindUserAsync() is not { } record) return call.NotFound(NoUser);

		call.Formatter.Print(call.Output, UserView.Of(call.Get<UserRegistry>(), record, call.Get<TimeProvider>()));
		return ExitCodes.Success;
	}

	private static async Task<int> SetAsync(CommandCall call)
	{
		if (await call.FindUserAsync() is not { } target) return call.NotFound(NoUser);

		if (await SetProfileAsync(call, target) is { } profileRefusal) return profileRefusal;
		if (await SetCredentialsAsync(call, target) is { } credentialsRefusal) return credentialsRefusal;

		call.Output.Ok("Updated.");
		return ExitCodes.Success;
	}

	private static async Task<int?> SetProfileAsync(CommandCall call, UserRecord target)
	{
		var ct = call.CancellationToken;
		var sender = call.Get<ISender>();
		var actor = await call.ByAsync();
		if (call.Text("username") is { } name && await sender.Send(new RenameUser(actor, target, name), ct) is
			    { IsOk: false } renamed)
			return call.Refuse(renamed);

		if (call.Text("country") is { } code)
		{
			if (!(code.Length == 2 && Enum.TryParse<Country>(code, true, out var country) && Enum.IsDefined(country)))
				throw CommandException.Invalid("--country must be a two-letter code.");
			if (await sender.Send(new SetUserCountry(actor, target, country), ct) is { IsOk: false } changed)
				return call.Refuse(changed);
		}

		if (call.Text("away") is { } away)
			await sender.Send(new SetUserAway(actor, target, away.Length == 0 ? null : away), ct);
		if (call.Bool("pmPrivate") is { } pm) await sender.Send(new SetUserPmPrivate(actor, target, pm), ct);
		return null;
	}

	private static async Task<int?> SetCredentialsAsync(CommandCall call, UserRecord target)
	{
		var ct = call.CancellationToken;
		var sender = call.Get<ISender>();
		var actor = await call.ByAsync();
		if (call.Mask("privileges") is { } privileges &&
		    await sender.Send(new SetUserPrivileges(actor, target, (UserPrivileges)privileges), ct) is
			    { IsOk: false } set)
			return call.Refuse(set);

		if (call.Text("password") is { } password &&
		    await sender.Send(new ChangeUserPassword(actor, target, CommandHelpers.Md5Of(password), null, false), ct) is
			    { IsOk: false } changedPassword)
			return call.Refuse(changedPassword);

		return null;
	}

	private static async Task<int> SetAvatarAsync(CommandCall call)
	{
		if (await call.FindUserAsync() is not { } target) return call.NotFound(NoUser);

		await using var stream = File.OpenRead(call.Required("file"));
		return call.Report(
			await call.Get<ISender>()
				.Send(new SetUserAvatar(await call.ByAsync(), target, stream), call.CancellationToken), "Avatar set.");
	}

	private static async Task<int> RemoveAvatarAsync(CommandCall call)
	{
		return await call.FindUserAsync() is not { } target
			? call.NotFound(NoUser)
			: call.Report(
				await call.Get<ISender>()
					.Send(new RemoveUserAvatar(await call.ByAsync(), target), call.CancellationToken),
				"Avatar removed.");
	}

	private static async Task<int> RelationsAsync(CommandCall call, RelationshipType type)
	{
		if (await call.FindUserAsync() is not { } target) return call.NotFound(NoUser);

		var (result, users) = type == RelationshipType.Friend
			? await call.Get<ISender>().Send(new ListFriends(await call.ByAsync(), target), call.CancellationToken)
			: await call.Get<ISender>().Send(new ListBlocks(await call.ByAsync(), target), call.CancellationToken);
		if (!result.IsOk) return call.Refuse(result);

		call.Formatter.PrintTable(call.Output, [.. users.Select(UserRef.Of)]);
		return ExitCodes.Success;
	}

	private static async Task<int> SilenceAsync(CommandCall call)
	{
		if (await call.FindUserAsync() is not { } target) return call.NotFound(NoUser);

		var endsAt = call.Get<TimeProvider>().GetUtcNow() + call.Duration("duration")!.Value;
		return call.Report(await call.Get<ISender>().Send(
			new SilenceUser(await call.ByAsync(), target, endsAt, call.Text("reason")), call.CancellationToken),
			"Silenced.");
	}

	private static async Task<int> UnsilenceAsync(CommandCall call)
	{
		if (await call.FindUserAsync() is not { } target) return call.NotFound(NoUser);

		return call.Report(await call.Get<ISender>().Send(
			new UnsilenceUser(await call.ByAsync(), target, null), call.CancellationToken), "Silence lifted.");
	}

	private static async Task<int> SessionsAsync(CommandCall call)
	{
		var page = call.Page();
		var state = (call.Text("state") ?? "all").ToLowerInvariant() switch
		{
			"all" => (SessionState?)SessionState.Any,
			"online" => SessionState.Active,
			"offline" => SessionState.Ended,
			_ => null
		} ?? throw CommandException.Invalid("--state must be all, online or offline.");
		var kinds = call.Mask("types") is { } types
			? Enum.GetValues<SessionType>().Where(type => (types & (ulong)type) != 0).ToList()
			: null;
		if (await call.FindUserAsync() is not { } target) return call.NotFound(NoUser);

		var (result, sessions) = await call.Get<ISender>()
			.Send(new ListUserSessions(await call.ByAsync(), target, page, state, kinds), call.CancellationToken);
		if (!result.IsOk) return call.Refuse(result);

		call.PrintPage(new Page<SessionView>([.. sessions!.Items.Select(SessionView.Of)],
			sessions.HasNext), page);
		return ExitCodes.Success;
	}

	private static async Task<int> SessionAsync(CommandCall call)
	{
		if (await call.FindUserAsync() is not { } target) return call.NotFound(NoUser);

		var (result, session) = await call.Get<ISender>()
			.Send(new GetUserSession(await call.ByAsync(), target, long.Parse(call.Word("sid"), CultureInfo.InvariantCulture)),
				call.CancellationToken);
		if (!result.IsOk) return call.Refuse(result);

		call.Formatter.Print(call.Output, SessionView.Of(session!));
		return ExitCodes.Success;
	}

	private static async Task<int> SessionConnectionsAsync(CommandCall call)
	{
		var page = call.Page();
		if (await call.FindUserAsync() is not { } target) return call.NotFound(NoUser);

		var (result, connections) = await call.Get<ISender>()
			.Send(new ListSessionConnections(await call.ByAsync(), target,
					long.Parse(call.Word("sid"), CultureInfo.InvariantCulture), page),
				call.CancellationToken);
		if (!result.IsOk) return call.Refuse(result);

		call.PrintPage(new Page<ConnectionView>([.. connections!.Items.Select(ConnectionView.Of)],
			connections.HasNext), page);
		return ExitCodes.Success;
	}

	private static async Task<int> CloseSessionAsync(CommandCall call)
	{
		return await call.FindUserAsync() is not { } target
			? call.NotFound(NoUser)
			: call.Report(
				await call.Get<ISender>()
					.Send(new CloseUserSession(await call.ByAsync(), target, long.Parse(call.Word("sid"), CultureInfo.InvariantCulture)),
						call.CancellationToken),
				"Session closed.");
	}

	private static async Task<int> CloseSessionsAsync(CommandCall call)
	{
		return await call.FindUserAsync() is not { } target
			? call.NotFound(NoUser)
			: call.Report(
				await call.Get<ISender>().Send(new CloseUserSessions(await call.ByAsync(), target, false),
					call.CancellationToken), "Sessions closed.");
	}

	private static async Task<int> DeleteAsync(CommandCall call)
	{
		return await call.FindUserAsync() is not { } target
			? call.NotFound(NoUser)
			: call.Report(
				await call.Get<ISender>().Send(new DeleteUser(await call.ByAsync(), target), call.CancellationToken),
				"User deleted.");
	}
}