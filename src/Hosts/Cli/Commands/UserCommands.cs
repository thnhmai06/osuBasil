using Basil.Application.Auth.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Domain.Users;
using Basil.Host.Shared.Views;

namespace Basil.Host.Cli.Commands;

/// <summary>The <c>users</c> commands: accounts, their avatars, friends, blocks, restrictions and sessions.</summary>
internal static class UserCommands
{
	private const string NoUser = "user";

	public static IEnumerable<CommandDef> Create()
	{
		yield return new CommandDef
		{
			Pattern = "users list", Route = "GET /v1/users", Offline = true, Summary = "Searches accounts.",
			Options = [new("query", "The user search syntax."), .. CommandHelpers.Paging], Run = ListAsync
		};
		yield return new CommandDef
		{
			Pattern = "users create", Route = "POST /v1/users", Offline = true, Summary = "Creates an account.",
			Options =
			[
				new("username", "The name of the account.", Required: true, Alias: "u"),
				new("password", "The plain password.", Required: true, Alias: "p", Secret: true),
				new("country", "A two-letter country code."),
				new("permissions", "The permissions, as a bitmask (default: the default permissions).", OptionKind.Mask),
				new("admin", "Give the account every permission.", OptionKind.Bool)
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
				new("username", "The new name."), new("country", "A two-letter country code."),
				new("away", "The away message; empty clears it."), new("pmPrivate", "Block private messages from strangers.", OptionKind.Bool),
				new("permissions", "The granted permissions, as a bitmask.", OptionKind.Mask),
				new("password", "A new plain password; every session of the account is closed.", Secret: true)
			],
			Run = SetAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> avatar", Route = "PUT /v1/users/{id}/avatar", Offline = true,
			Summary = "Sets the avatar from an image file.",
			Options = [new("file", "The path of the image.", OptionKind.Path, true)], Run = SetAvatarAsync
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
				Summary = $"Lists the accounts this account has {(kind == RelationshipType.Friend ? "as friends" : "blocked")}.",
				Run = call => RelationsAsync(call, kind)
			};
			yield return new CommandDef
			{
				Pattern = $"users <id:int> {word} <other:int> put", Route = $"PUT /v1/users/{{id}}/{word}/{{otherId}}",
				Offline = true, Summary = kind == RelationshipType.Friend ? "Makes the account a friend." : "Blocks the account.",
				Run = call => HoldAsync(call, kind, true)
			};
			yield return new CommandDef
			{
				Pattern = $"users <id:int> {word} <other:int> delete", Route = $"DELETE /v1/users/{{id}}/{word}/{{otherId}}",
				Offline = true, Summary = kind == RelationshipType.Friend ? "Removes the friend." : "Removes the block.",
				Run = call => HoldAsync(call, kind, false)
			};
		}

		yield return new CommandDef
		{
			Pattern = "users <id:int> restrictions list", Route = "GET /v1/users/{id}/restrictions", Offline = true,
			Summary = "Lists the restrictions an account has had.", Run = RestrictionsAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> restrictions create", Route = "POST /v1/users/{id}/restrictions", Offline = true,
			Summary = "Restricts an account.",
			Options =
			[
				new("permissions", "The suspended permissions, as a bitmask.", OptionKind.Mask, true),
				new("duration", "How long it lasts, such as 30m, 12h or 7d; none means until lifted.", OptionKind.Duration),
				new("reason", "Why.")
			],
			Run = RestrictAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> restrictions <rid:int> lift", Route = "POST /v1/users/{id}/restrictions/{rid}/lift",
			Offline = true, Summary = "Ends a restriction now and keeps it in the history.", Run = LiftAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> restrictions <rid:int> delete", Route = "DELETE /v1/users/{id}/restrictions/{rid}",
			Offline = true, Summary = "Ends a restriction and removes it from the history.", Run = DeleteRestrictionAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> sessions list", Route = "GET /v1/users/{id}/sessions", Offline = true,
			Summary = "Lists the sessions of an account.",
			Options =
			[
				new("state", "all, online or offline (default all)."),
				new("types", "The connection kinds, as a bitmask.", OptionKind.Mask), .. CommandHelpers.Paging
			],
			Run = SessionsAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> sessions <sid:guid>", Route = "GET /v1/users/{id}/sessions/{sid}", Offline = true,
			Summary = "Shows one session.", Run = SessionAsync
		};
		yield return new CommandDef
		{
			Pattern = "users <id:int> sessions <sid:guid> delete", Route = "DELETE /v1/users/{id}/sessions/{sid}",
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
		var found = await call.Get<IUserRepository>().ListAsync(UserQuery.Parse(call.Text("query") ?? string.Empty), page,
			call.CancellationToken);
		call.PrintPage(new Basil.Application.Common.Page<UserView>(
			[.. found.Items.Select(user => UserView.Of(registry, user, clock))], found.Total), page);
		return ExitCodes.Success;
	}

	private static async Task<int> CreateAsync(CommandCall call)
	{
		var data = new UserData
		{
			Name = call.Required("username"),
			Permissions = call.Bool("admin") == true
				? UserPermissionsGroups.All
				: call.Mask("permissions") is { } mask ? (UserPermissions)mask : UserPermissionsGroups.Default
		};
		if (call.Text("country") is { } code)
			data.Country = Enum.TryParse<Country>(code, true, out var country) && code.Length == 2 && Enum.IsDefined(country)
				? country
				: throw CommandException.Invalid("--country must be a two-letter code.");

		var by = await call.ByAsync();
		var (record, result) = await call.Get<UserRegistry>().ScopeTo(by)
			.CreateAccountAsync(data, CommandHelpers.Md5Of(call.Required("password")), call.CancellationToken);
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

		var restrictions = await call.Get<IRestrictionRepository>().ListAsync(record, call.CancellationToken);
		call.Formatter.Print(call.Output, UserView.Of(call.Get<UserRegistry>(), record, call.Get<TimeProvider>(), restrictions));
		return ExitCodes.Success;
	}

	private static async Task<int> SetAsync(CommandCall call)
	{
		if (await call.ScopeUserAsync() is not { } scoped) return call.NotFound(NoUser);

		if (await SetProfileAsync(call, scoped) is { } profileRefusal) return profileRefusal;
		if (await SetCredentialsAsync(call, scoped) is { } credentialsRefusal) return credentialsRefusal;

		call.Output.Ok("Updated.");
		return ExitCodes.Success;
	}

	private static async Task<int?> SetProfileAsync(CommandCall call, ScopedUser scoped)
	{
		var ct = call.CancellationToken;
		if (call.Text("username") is { } name && await scoped.Profile.RenameAsync(name, ct) is { IsOk: false } renamed)
			return call.Refuse(renamed);

		if (call.Text("country") is { } code)
		{
			if (!(code.Length == 2 && Enum.TryParse<Country>(code, true, out var country) && Enum.IsDefined(country)))
				throw CommandException.Invalid("--country must be a two-letter code.");
			if (await scoped.Profile.SetCountryAsync(country, ct) is { IsOk: false } changed) return call.Refuse(changed);
		}

		if (call.Text("away") is { } away) scoped.Profile.SetAway(away.Length == 0 ? null : away);
		if (call.Bool("pmPrivate") is { } pm) scoped.Profile.SetPmPrivate(pm);
		return null;
	}

	private static async Task<int?> SetCredentialsAsync(CommandCall call, ScopedUser scoped)
	{
		var ct = call.CancellationToken;
		if (call.Mask("permissions") is { } permissions &&
		    await scoped.Permissions.SetAsync((UserPermissions)permissions, ct) is { IsOk: false } set)
			return call.Refuse(set);

		if (call.Text("password") is { } password &&
		    await scoped.Credentials.ChangePasswordAsync(CommandHelpers.Md5Of(password), null, false, ct) is { IsOk: false } changedPassword)
			return call.Refuse(changedPassword);

		return null;
	}

	private static async Task<int> SetAvatarAsync(CommandCall call)
	{
		if (await call.ScopeUserAsync() is not { } scoped) return call.NotFound(NoUser);

		await using var stream = File.OpenRead(call.Required("file"));
		return call.Report(await scoped.Profile.SetAvatarAsync(stream, call.CancellationToken), "Avatar set.");
	}

	private static async Task<int> RemoveAvatarAsync(CommandCall call)
	{
		return await call.ScopeUserAsync() is not { } scoped
			? call.NotFound(NoUser)
			: call.Report(await scoped.Profile.RemoveAvatarAsync(call.CancellationToken), "Avatar removed.");
	}

	private static async Task<int> RelationsAsync(CommandCall call, RelationshipType type)
	{
		if (await call.ScopeUserAsync() is not { } scoped) return call.NotFound(NoUser);

		var (result, users) = type == RelationshipType.Friend
			? await scoped.Relationships.FriendsAsync(call.CancellationToken)
			: await scoped.Relationships.BlocksAsync(call.CancellationToken);
		if (!result.IsOk) return call.Refuse(result);

		call.Formatter.PrintTable(call.Output, [.. users.Select(UserRef.Of)]);
		return ExitCodes.Success;
	}

	private static async Task<int> HoldAsync(CommandCall call, RelationshipType type, bool present)
	{
		var owner = await call.FindUserAsync();
		if (owner is null) return call.NotFound(NoUser);

		if (await call.ScopeUserAsync("other") is not { } scoped) return call.NotFound(NoUser);

		return call.Report(await scoped.Relationships.HeldByAsync(owner, type, present, call.CancellationToken),
			(present ? "Set " : "Removed ") + (type == RelationshipType.Friend ? "friend." : "block."));
	}

	private static async Task<int> RestrictionsAsync(CommandCall call)
	{
		if (await call.FindUserAsync() is not { } record) return call.NotFound(NoUser);

		var list = await call.Get<IRestrictionRepository>().ListAsync(record, call.CancellationToken);
		call.Formatter.PrintTable(call.Output, [.. list.Select(RestrictionView.Of)]);
		return ExitCodes.Success;
	}

	private static async Task<int> RestrictAsync(CommandCall call)
	{
		if (await call.ScopeUserAsync() is not { } scoped) return call.NotFound(NoUser);

		var duration = call.Duration("duration");
		var endsAt = duration is { } length ? call.Get<TimeProvider>().GetUtcNow() + length : (DateTimeOffset?)null;
		var (result, restriction) = await scoped.Restrictions.RestrictAsync((UserPermissions)call.Mask("permissions")!.Value,
			endsAt, call.Text("reason"), call.CancellationToken);
		if (!result.IsOk) return call.Refuse(result);

		call.Output.Ok($"Restricted with restriction {restriction!.Id}.");
		return ExitCodes.Success;
	}

	private static async Task<int> LiftAsync(CommandCall call)
	{
		if (await call.ScopeUserAsync() is not { } scoped) return call.NotFound(NoUser);

		return await call.Get<IRestrictionRepository>().GetAsync(call.Id("rid"), call.CancellationToken) is not { } restriction
			? call.NotFound("restriction")
			: call.Report(await scoped.Restrictions.LiftAsync(restriction, null, call.CancellationToken), "Restriction lifted.");
	}

	private static async Task<int> DeleteRestrictionAsync(CommandCall call)
	{
		if (await call.ScopeUserAsync() is not { } scoped) return call.NotFound(NoUser);

		return await call.Get<IRestrictionRepository>().GetAsync(call.Id("rid"), call.CancellationToken) is not { } restriction
			? call.NotFound("restriction")
			: call.Report(await scoped.Restrictions.DeleteAsync(restriction, null, call.CancellationToken), "Restriction deleted.");
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
			? Enum.GetValues<ConnectionType>().Where(type => (types & (ulong)type) != 0).ToList()
			: null;
		if (await call.ScopeUserAsync() is not { } scoped) return call.NotFound(NoUser);

		var (result, sessions) = await scoped.Sessions.ListAsync(page, state, kinds, call.CancellationToken);
		if (!result.IsOk) return call.Refuse(result);

		call.PrintPage(new Basil.Application.Common.Page<SessionView>([.. sessions!.Items.Select(SessionView.Of)],
			sessions.Total), page);
		return ExitCodes.Success;
	}

	private static async Task<int> SessionAsync(CommandCall call)
	{
		if (await call.ScopeUserAsync() is not { } scoped) return call.NotFound(NoUser);

		var (result, session) = await scoped.Sessions.GetAsync(Guid.Parse(call.Word("sid")), call.CancellationToken);
		if (!result.IsOk) return call.Refuse(result);

		call.Formatter.Print(call.Output, SessionView.Of(session!));
		return ExitCodes.Success;
	}

	private static async Task<int> CloseSessionAsync(CommandCall call)
	{
		return await call.ScopeUserAsync() is not { } scoped
			? call.NotFound(NoUser)
			: call.Report(await scoped.Sessions.CloseAsync(Guid.Parse(call.Word("sid")), call.CancellationToken), "Session closed.");
	}

	private static async Task<int> CloseSessionsAsync(CommandCall call)
	{
		return await call.ScopeUserAsync() is not { } scoped
			? call.NotFound(NoUser)
			: call.Report(await scoped.Sessions.CloseAllAsync(false, call.CancellationToken), "Sessions closed.");
	}

	private static async Task<int> DeleteAsync(CommandCall call)
	{
		return await call.ScopeUserAsync() is not { } scoped
			? call.NotFound(NoUser)
			: call.Report(await scoped.DeleteAsync(call.CancellationToken), "User deleted.");
	}
}
