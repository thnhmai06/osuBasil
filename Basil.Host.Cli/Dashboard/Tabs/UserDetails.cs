using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Basil.Application.Users;
using System.Globalization;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Messages;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed class UserDetails(
	IServiceProvider services,
	CommandActions actions,
	BeatmapDetails beatmaps,
	CliOptions options,
	Dialogs dialogs,
	Glyphs glyphs,
	TimeProvider clock,
	Action<string> openChat)
{
	private const int RowsShown = 20;

	public async Task<DetailView> OfAsync(int id)
	{
		var record = await services.GetRequiredService<ISender>().Send(new GetUser(id));
		return Show(record, id.ToString(CultureInfo.InvariantCulture));
	}

	public async Task<DetailView> OfNameAsync(string name)
	{
		var record = await services.GetRequiredService<ISender>().Send(new FindUserByName(name));
		return Show(record, name);
	}

	private DetailView Show(UserRecord? record, string title)
	{
		return record is null
			? new DetailView(title, [("User", "does not exist", Ink.Warning)], [])
			: Of(ItemOf(record));
	}

	public DetailView Of(UserItem user)
	{
		var room = user.Room.Length == 0 ? "-" : user.Room;
		var managed = user.Managed.Length == 0 ? string.Empty : $" ({user.Managed})";
		var ink = UserInk.Of((UserPrivileges)user.Mask);
		var online = user.Connections.Length > 0;
		var fields = new List<(string, string, Ink)>
		{
			("ID", user.Id.ToString(CultureInfo.InvariantCulture), Ink.Text), ("FullName", user.Name, ink),
			("Country", user.Country, Ink.Text), ("Privileges", $"{user.Privileges} ({user.Mask})", ink),
			("Connections", online ? user.Connections : "offline", Ink.Info), ("Room", room + managed, Ink.Text),
			("Activity", user.Activity.Length == 0 ? "-" : user.Activity, Ink.Text)
		};
		if (user.Silenced) fields.Add(("Silenced", "yes", Ink.Warning));
		var reload = () => OfAsync(user.Id);
		var list = new List<DetailAction>
		{
			DetailAction.OpenAsync("Sessions", () => SessionsAsync(user)),
			DetailAction.OpenAsync("Scores", () => ScoresAsync(user)),
			DetailAction.OpenAsync("Friends", () => RelationsAsync(user, true)),
			DetailAction.OpenAsync("Blocks", () => RelationsAsync(user, false))
		};
		// The bot is a system account: what it has can be read, nothing can be done to it.
		if (user.Id == BasilBot.Id) return new DetailView(user.Name, fields, list) { Reload = reload };

		var id = user.Id;
		if (online) list.Add(new DetailAction("Send message", () => openChat(user.Name)));
		list.Add(new DetailAction("Edit profile", () => EditProfile(user)));
		list.Add(new DetailAction("Change password", () => AskPassword(user)));
		list.Add(new DetailAction("Privileges", () => AskPrivileges(user)));
		list.Add(user.Silenced
			? new DetailAction("Unsilence", () => actions.Run($"users {id} unsilence"))
			: new DetailAction("Silence", () => AskSilence(id)));
		list.Add(new DetailAction("Set avatar", () => AskAvatar(user)));
		list.Add(new DetailAction("Remove avatar", () => actions.When(() => HasAvatarAsync(id), "No avatar to remove.",
			() => dialogs.Confirm("Remove avatar",
				$"Remove the avatar of {user.Name} (id {id})? The default image is shown instead.", "Remove",
				() => actions.Run($"users {id} avatar delete"))), DetailActionKind.Destructive));
		if (online)
			list.Add(new DetailAction("Kick", () => dialogs.Confirm("Kick",
				$"Close every session of {user.Name} (id {id})? Their clients are disconnected, and API tokens " +
				"stop working. They can sign in again with their password.", "Kick",
				() => actions.Run($"users {id} sessions delete")), DetailActionKind.Destructive));
		list.Add(new DetailAction("Delete account", () => dialogs.Confirm("Delete account",
			$"Delete user {user.Name} (id {id})? The account is hidden from sign-in and every listing, its privileges are " +
			"cleared and every session is closed, which disconnects the client. Its scores are kept.", "Delete",
			() => actions.Run($"users {id} delete")), DetailActionKind.Destructive));
		return new DetailView(user.Name, fields, list) { Reload = reload };
	}

	public DetailView Of(SessionItem session)
	{
		var fields = new List<(string, string, Ink)>
		{
			("Session", session.Id.ToString(CultureInfo.InvariantCulture), Ink.Text),
			("User", session.UserName, UserInk.Of((UserPrivileges)session.Mask)), ("Kind", session.Kind, Ink.Text),
			("Last seen", session.LastSeen, session.Live ? Ink.Success : Ink.Text),
			("Since", SessionItem.Time(session.Started), Ink.Text), ("IP", session.Ip, Ink.Text),
			("Client", session.Client.Length == 0 ? "-" : session.Client, Ink.Text)
		};
		if (session.EndReason.Length > 0) fields.Add(("Ended", session.EndReason, Ink.Text));
		var actionList = new List<DetailAction>
		{
			DetailAction.OpenAsync("User", () => OfAsync(session.UserId))
		};
		if (session.Live)
			actionList.Add(new DetailAction("Close session", () => dialogs.Confirm("Close session",
					$"Close session {session.Id} of {session.UserName}? The client is disconnected; the API token of " +
					"this session stops working. The account can sign in again with its password.", "Close session",
					() => actions.Run($"users {session.UserId} sessions {session.Id} delete")),
				DetailActionKind.Destructive));
		var id = session.Id.ToString(CultureInfo.InvariantCulture);
		return new DetailView($"{session.Kind} session {(id.Length > 6 ? id[..4] + "…" : id)}", fields, actionList)
		{
			Reload = () => SessionAsync(session.UserId, session.Id)
		};
	}

	private UserItem ItemOf(UserRecord record)
	{
		services.GetRequiredService<SessionRegistry>().Online.TryGetValue(record, out var online);
		return UsersSource.Item(services.GetRequiredService<Lobby>(), record, online, clock.GetUtcNow());
	}

	private async Task<DetailView> SessionAsync(int userId, long id)
	{
		var actor = await services.GetRequiredService<BotSessions>().OperateAsync();
		var (outcome, record) = await services.GetRequiredService<ISender>()
			.Send(new GetUserSession(actor, userId, id));
		return outcome == UserActionResult.Ok && record is not null
			? Of(SessionItem.Of(record, clock.GetUtcNow()))
			: new DetailView(id.ToString(CultureInfo.InvariantCulture), [("Session", "does not exist", Ink.Warning)],
				[]);
	}

	// The newest sessions of the account, read once as the console command does, shown as the table of the Sessions tab.
	private async Task<DetailView> SessionsAsync(UserItem user)
	{
		var actor = await services.GetRequiredService<BotSessions>().OperateAsync();
		var (outcome, page) = await services.GetRequiredService<ISender>().Send(new ListUserSessions(actor,
			user.Id, new PageRequest(0, RowsShown), SessionFilter.Any, null));
		if (outcome != UserActionResult.Ok || page is null)
			return new DetailView("Sessions", [("Sessions", "not available", Ink.Warning)], []);

		var now = clock.GetUtcNow();
		var items = page.Items.Select(record => SessionItem.Of(record, now)).ToList();
		return new DetailView("Sessions", [], [], push => NestedTable.Of(SessionColumns.Of(null),
			session => session.Id.ToString(CultureInfo.InvariantCulture),
			[.. SessionItem.Kinds.Select(kind => GroupOf(kind, items))], glyphs, options.NoColor, clock,
			session => push(Of(session)))) { Reload = () => SessionsAsync(user) };
	}

	private async Task<DetailView> ScoresAsync(UserItem user)
	{
		var sections = await new ScoresSource(services).LoadAsync($"user:{user.Id}", RowsShown, CancellationToken.None);
		return new DetailView("Scores", [], [], push => NestedTable.Of(ScoresTab.Columns(null), score =>
				score.Id.ToString(CultureInfo.InvariantCulture),
			[
				.. sections.Select(section => new Section<ScoreItem>(section.Title,
					section.Items.Count.ToString(CultureInfo.InvariantCulture), section.Items))
			], glyphs, options.NoColor, clock,
			score => push(ScoresTab.Detail(score, this, beatmaps)))) { Reload = () => ScoresAsync(user) };
	}

	// The accounts this one has as friends or has blocked.
	private async Task<DetailView> RelationsAsync(UserItem user, bool friends)
	{
		var title = friends ? "Friends" : "Blocks";
		var actor = await services.GetRequiredService<BotSessions>().OperateAsync();
		var sender = services.GetRequiredService<ISender>();
		var target = user.Id;
		var (outcome, records) = friends
			? await sender.Send(new ListFriends(actor, target))
			: await sender.Send(new ListBlocks(actor, target));
		if (outcome != UserActionResult.Ok)
			return new DetailView(title, [(title, "not available", Ink.Warning)], []);

		var items = records.Select(ItemOf).ToList();
		IReadOnlyList<Column<UserItem>> columns =
		[
			new("ID", 8, u => u.Id.ToString(CultureInfo.InvariantCulture)) { Right = true },
			new("FullName", 0, u => u.Name, u => UserInk.Of((UserPrivileges)u.Mask)),
			new("Country", 8, u => u.Country, _ => Ink.Muted),
			new("Connections", 14, u => u.Connections, _ => Ink.Info)
		];
		return new DetailView(title, [], [], push => NestedTable.Of(columns,
			u => u.Id.ToString(CultureInfo.InvariantCulture),
			[new Section<UserItem>(title, items.Count.ToString(CultureInfo.InvariantCulture), items)], glyphs,
			options.NoColor,
			clock, u => push(Of(u)))) { Reload = () => RelationsAsync(user, friends) };
	}

	private static Section<SessionItem> GroupOf(SessionType kind, IReadOnlyList<SessionItem> items)
	{
		var of = items.Where(item => item.Type == kind).ToList();
		return new Section<SessionItem>(SessionItem.KindName(kind), of.Count.ToString(CultureInfo.InvariantCulture),
			of);
	}

	private void AskSilence(int id)
	{
		dialogs.Form("Silence", [new FormField("Duration", "30m"), new FormField("Reason")], values =>
		{
			var reason = values[1].Length == 0 ? string.Empty : $" --reason {CommandActions.Quote(values[1])}";
			actions.Run($"users {id} silence --duration {CommandActions.Quote(values[0])}{reason}");
		});
	}

	// Only what the operator changed is sent; an empty away message or private-message answer leaves it as it is.
	private void EditProfile(UserItem user)
	{
		dialogs.Form($"Edit {user.Name}",
		[
			new FormField("FullName", user.Name), new FormField("Country", user.Country), new FormField("Away message"),
			new FormField("Block PMs (yes/no)")
		], values =>
		{
			var line = $"users {user.Id} set";
			if (values[0].Length > 0 && values[0] != user.Name)
				line += $" --username {CommandActions.Quote(values[0])}";
			if (values[1].Length > 0 && !values[1].Equals(user.Country, StringComparison.OrdinalIgnoreCase))
				line += $" --country {CommandActions.Quote(values[1])}";
			if (values[2].Length > 0) line += $" --away {CommandActions.Quote(values[2])}";
			if (values[3].Length > 0) line += $" --pmPrivate {(values[3].StartsWith('y') ? "true" : "false")}";
			if (line == $"users {user.Id} set") actions.Notify("Nothing was changed.");
			else actions.Run(line);
		});
	}

	private void AskPassword(UserItem user)
	{
		dialogs.Form($"Password of {user.Name}", [new FormField("New password", Secret: true)], values =>
		{
			if (values[0].Length == 0) actions.Notify("The password is empty.");
			else actions.Run($"users {user.Id} set --password {CommandActions.Quote(values[0])}");
		});
	}

	private async Task<bool> HasAvatarAsync(int id)
	{
		await using var stored = await services.GetRequiredService<ISender>().Send(new OpenUserAvatar(id));
		return stored is not null;
	}

	private void AskAvatar(UserItem user)
	{
		dialogs.Form($"Avatar of {user.Name}", [new FormField("Image file")], values =>
		{
			if (values[0].Length == 0) actions.Notify("No file was given.");
			else actions.Run($"users {user.Id} avatar --file {CommandActions.Quote(values[0])}");
		});
	}

	// Each privilege flag is a box; the account then holds exactly the ones ticked.
	private void AskPrivileges(UserItem user)
	{
		dialogs.Privileges($"Privileges of {user.Name}", (UserPrivileges)user.Granted,
			granted => actions.Run($"users {user.Id} set --privileges {(byte)granted}"));
	}
}