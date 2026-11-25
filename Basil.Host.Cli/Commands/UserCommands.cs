using Basil.Application.Sessions.Messages;
using Basil.Application.Users.Registry;
using Basil.Application.Users;
using System.Globalization;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Basil.Host.Shared.Views;
using Mediator;

namespace Basil.Host.Cli.Commands;

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
				new OptionDef("privileges", "The privileges as names joined by commas, such as player,supporter (default: the default privileges).",
					OptionKind.Mask, Choices: FlagNames.Of(typeof(UserPrivileges)).Select(flag => flag.Name).ToList()),
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
				new OptionDef("privileges", "The granted privileges as names joined by commas, such as player,supporter.",
					OptionKind.Mask, Choices: FlagNames.Of(typeof(UserPrivileges)).Select(flag => flag.Name).ToList()),
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
				new OptionDef("state", "all, online or offline (default all).", Choices: ["all", "online", "offline"]),
				new OptionDef("types", "The connection kinds as names joined by commas, such as stable,irc.", OptionKind.Mask,
					Choices: FlagNames.Of(typeof(SessionType)).Select(flag => flag.Name).ToList()), .. CommandHelpers.Paging
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
		var sender = call.Get<ISender>();
		var found = await sender.Send(new SearchUsers(await call.ByAsync(),
			UserQuery.Parse(call.Text("query") ?? string.Empty), page), call.CancellationToken);
		call.PrintPage(new Page<UserView>(await UserView.OfAsync(sender, found.Items, call.CancellationToken),
			found.HasNext), page);
		return ExitCodes.Success;
	}

	private static async Task<int> CreateAsync(CommandCall call)
	{
		var data = new UserData
		{
			Name = call.Required("username")
		};
		var privileges = call.Bool("admin") == true
			? UserPrivilegesExtensions.All
			: call.Flags<UserPrivileges>("privileges");
		if (call.Text("country") is { } code)
			data.Country = Enum.TryParse<Country>(code, true, out var country) && code.Length == 2 &&
			               Enum.IsDefined(country)
				? country
				: throw CommandException.Invalid("--country must be a two-letter code.");

		var actor = await call.ByAsync();
		var (record, result) = await call.Get<ISender>().Send(new CreateAccount(actor, data,
			CommandHelpers.Md5Of(call.Required("password")), privileges), call.CancellationToken);
		return result == UserActionResult.Ok
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
		if (await call.Get<ISender>().Send(new GetUser(call.UserOf()), call.CancellationToken) is not { } record)
			return call.NotFound(NoUser);

		call.Formatter.Print(call.Output,
			(await UserView.OfAsync(call.Get<ISender>(), [record], call.CancellationToken))[0]);
		return ExitCodes.Success;
	}

	private static async Task<int> SetAsync(CommandCall call)
	{
		var target = call.UserOf();
		if (await UpdateAsync(call, target) is { } refusal) return refusal;
		if (await SetPasswordAsync(call, target) is { } passwordRefusal) return passwordRefusal;

		call.Output.Ok($"Updated {await LabelAsync(call, target)}: {string.Join(", ", call.Options.Keys)}.");
		return ExitCodes.Success;
	}

	// "peppy (id 2)", so that a confirmation says which account it is about.
	private static async Task<string> LabelAsync(CommandCall call, int id)
	{
		return await call.Get<ISender>().Send(new GetUser(id), call.CancellationToken) is { } record
			? $"{record.Value.Name} (id {id})"
			: $"account {id}";
	}

	private static async Task<int?> UpdateAsync(CommandCall call, int target)
	{
		Country? country = null;
		if (call.Text("country") is { } code)
			country = code.Length == 2 && Enum.TryParse<Country>(code, true, out var parsed) && Enum.IsDefined(parsed)
				? parsed
				: throw CommandException.Invalid("--country must be a two-letter code.");

		var change = new UserChange(call.Text("username"), country, call.Text("away"), call.Bool("pmPrivate"),
			call.Flags<UserPrivileges>("privileges"));
		return await call.Get<ISender>().Send(new UpdateUser(await call.ByAsync(), target, change),
			call.CancellationToken) is var result and not UserActionResult.Ok
			? call.Refuse(result)
			: null;
	}

	private static async Task<int?> SetPasswordAsync(CommandCall call, int target)
	{
		if (call.Text("password") is { } password &&
		    await call.Get<ISender>().Send(
			    new ChangeUserPassword(await call.ByAsync(), target, CommandHelpers.Md5Of(password), null, false),
			    call.CancellationToken) is var changedPassword and not UserActionResult.Ok)
			return call.Refuse(changedPassword);

		return null;
	}

	private static async Task<int> SetAvatarAsync(CommandCall call)
	{
		await using var stream = CommandHelpers.OpenFile(call.Required("file"));
		return call.Report(
			await call.Get<ISender>()
				.Send(new SetUserAvatar(await call.ByAsync(), call.UserOf(), stream), call.CancellationToken),
			$"Set the avatar of {await LabelAsync(call, call.UserOf())}.");
	}

	private static async Task<int> RemoveAvatarAsync(CommandCall call)
	{
		return call.Report(
			await call.Get<ISender>()
				.Send(new RemoveUserAvatar(await call.ByAsync(), call.UserOf()), call.CancellationToken),
			$"Removed the avatar of {await LabelAsync(call, call.UserOf())}.");
	}

	private static async Task<int> RelationsAsync(CommandCall call, RelationshipType type)
	{
		var target = call.UserOf();
		var (result, users) = type == RelationshipType.Friend
			? await call.Get<ISender>().Send(new ListFriends(await call.ByAsync(), target), call.CancellationToken)
			: await call.Get<ISender>().Send(new ListBlocks(await call.ByAsync(), target), call.CancellationToken);
		if (result != UserActionResult.Ok) return call.Refuse(result);

		call.Formatter.PrintTable(call.Output, [.. users.Select(UserRef.Of)]);
		return ExitCodes.Success;
	}

	private static async Task<int> SilenceAsync(CommandCall call)
	{
		var endsAt = call.Get<TimeProvider>().GetUtcNow() + call.Duration("duration")!.Value;
		return call.Report(await call.Get<ISender>().Send(
				new SilenceUser(await call.ByAsync(), call.UserOf(), endsAt, call.Text("reason")),
				call.CancellationToken),
			$"Silenced {await LabelAsync(call, call.UserOf())} for {call.Text("duration")}.");
	}

	private static async Task<int> UnsilenceAsync(CommandCall call)
	{
		return call.Report(await call.Get<ISender>().Send(
			new UnsilenceUser(await call.ByAsync(), call.UserOf(), null), call.CancellationToken), $"Lifted the silence of {await LabelAsync(call, call.UserOf())}.");
	}

	private static async Task<int> SessionsAsync(CommandCall call)
	{
		var page = call.Page();
		var state = (call.Text("state") ?? "all").ToLowerInvariant() switch
		{
			"all" => (SessionFilter?)SessionFilter.Any,
			"online" => SessionFilter.Active,
			"offline" => SessionFilter.Ended,
			_ => null
		} ?? throw CommandException.Invalid("--state must be all, online or offline.");
		var kinds = call.Flags<SessionType>("types") is { } types
			? Enum.GetValues<SessionType>().Where(type => types.HasFlag(type)).ToList()
			: null;
		var (result, sessions) = await call.Get<ISender>()
			.Send(new ListUserSessions(await call.ByAsync(), call.UserOf(), page, state, kinds),
				call.CancellationToken);
		if (result != UserActionResult.Ok) return call.Refuse(result);

		call.PrintPage(new Page<SessionView>([.. sessions!.Items.Select(SessionView.Of)],
			sessions.HasNext), page);
		return ExitCodes.Success;
	}

	private static async Task<int> SessionAsync(CommandCall call)
	{
		var (result, session) = await call.Get<ISender>()
			.Send(
				new GetUserSession(await call.ByAsync(), call.UserOf(),
					long.Parse(call.Word("sid"), CultureInfo.InvariantCulture)),
				call.CancellationToken);
		if (result != UserActionResult.Ok) return call.Refuse(result);

		call.Formatter.Print(call.Output, SessionView.Of(session!));
		return ExitCodes.Success;
	}

	private static async Task<int> CloseSessionAsync(CommandCall call)
	{
		return call.Report(
			await call.Get<ISender>()
				.Send(new CloseUserSession(await call.ByAsync(), call.UserOf(),
						long.Parse(call.Word("sid"), CultureInfo.InvariantCulture)),
					call.CancellationToken),
			$"Closed session {call.Word("sid")} of {await LabelAsync(call, call.UserOf())}.", "session");
	}

	private static async Task<int> CloseSessionsAsync(CommandCall call)
	{
		return call.Report(
			await call.Get<ISender>().Send(new CloseUserSessions(await call.ByAsync(), call.UserOf(), false),
				call.CancellationToken), $"Closed every session of {await LabelAsync(call, call.UserOf())}.");
	}

	private static async Task<int> DeleteAsync(CommandCall call)
	{
		return call.Report(
			await call.Get<ISender>().Send(new DeleteUser(await call.ByAsync(), call.UserOf()), call.CancellationToken),
			$"Deleted account {call.UserOf()}.", NoUser);
	}
}