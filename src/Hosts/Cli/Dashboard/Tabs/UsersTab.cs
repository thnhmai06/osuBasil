using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>One account in the users table.</summary>
/// <param name="Id">The account id.</param>
/// <param name="Name">The account name.</param>
/// <param name="Country">The two-letter country code.</param>
/// <param name="Privileges">The privilege flags, as text.</param>
/// <param name="Mask">The privilege flags as a number.</param>
/// <param name="Connections">The kinds of connection it holds, empty when it is offline.</param>
/// <param name="Room">The room it sits in, or empty.</param>
/// <param name="Managed">The rooms it created or referees.</param>
/// <param name="Activity">What the osu! client reports it is doing, or empty.</param>
/// <param name="Silenced">Whether it is silenced now.</param>
internal sealed record UserItem(int Id, string Name, string Country, string Privileges, ulong Mask,
	string Connections, string Room, string Managed, string Activity, bool Silenced);

/// <summary>Reads the accounts: the ones online from memory, the others from the database.</summary>
internal sealed class UsersSource(IServiceProvider services, TimeProvider clock) : IListSource<UserItem>
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<SectionData<UserItem>>> LoadAsync(string query, int limit, CancellationToken ct)
	{
		var registry = services.GetRequiredService<UserRegistry>();
		var lobby = services.GetRequiredService<Lobby>();
		var parsed = UserQuery.Parse(query);
		var now = clock.GetUtcNow();
		var online = registry.Online.Values.Where(user => !user.Record.IsBasilBot())
			.Where(user => Matches(parsed, user.Record.Id, user.Record.Value.Name,
				user.Record.Value.Country.ToAcronym()))
			.OrderBy(user => user.Record.Id)
			.Select(user => Item(lobby, user.Record, user, now)).ToList();
		var onlineIds = online.Select(item => item.Id).ToHashSet();
		var actor = await registry.OperateAsync(ct);
		var found = await services.GetRequiredService<ISender>()
			.Send(new SearchUsers(actor, parsed, new PageRequest(0, limit)), ct);
		var offline = found.Items.Where(record => !onlineIds.Contains(record.Id) && !record.IsBasilBot())
			.Select(record => Item(lobby, record, null, now)).ToList();
		return
		[
			new SectionData<UserItem>("Online", online, false),
			new SectionData<UserItem>("Offline", offline, found.HasNext)
		];
	}

	private static bool Matches(UserQuery query, int id, string name, string country)
	{
		return (query.Text is not { Length: > 0 } text || name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
		        text == id.ToString()) &&
		       (query.Countries is not { Count: > 0 } countries ||
		        countries.Any(c => string.Equals(c.ToAcronym(), country, StringComparison.OrdinalIgnoreCase)));
	}

	private static UserItem Item(Lobby lobby, UserRecord record, Basil.Application.Users.Objects.User? user,
		DateTimeOffset now)
	{
		var mask = user is null ? (ulong)record.Value.Privileges : (ulong)user.EffectivePrivileges(now);
		var room = user is null
			? string.Empty
			: lobby.Rooms.Values.FirstOrDefault(r =>
				r.Slots.Any(slot => slot.Player is not null && ReferenceEquals(slot.Player.User, user)))?.Id is { } id
				? $"#{id}"
				: string.Empty;
		var managed = string.Join(", ", lobby.Rooms.Values
			.Where(r => r.Authority.Creator.Id == record.Id || r.Authority.Referees.Any(x => x.Id == record.Id))
			.Select(r => $"#{r.Id}"));
		return new UserItem(record.Id, record.Value.Name, record.Value.Country.ToAcronym().ToUpperInvariant(),
			((UserPrivileges)mask).ToString(), mask,
			user is null
				? string.Empty
				: string.Join(' ', user.Connections.Select(c => c.Type.ToString().ToLowerInvariant()).Distinct()),
			room, managed, user?.Stable?.Status.Activity.ToString() ?? string.Empty, record.Value.IsSilenced(now));
	}
}

/// <summary>The accounts: who is online and who is not, with what can be done to each.</summary>
internal sealed class UsersTab : ITabView, ISelectable
{
	private readonly ListPage<UserItem> _page;
	private bool _opened;

	/// <summary>Creates the tab.</summary>
	public UsersTab(IServiceProvider services, ConsoleTab console, Action showConsole, CliOptions options,
		Glyphs glyphs, TimeProvider clock, Action<Action> post, ILogger logger)
	{
		var colours = options.NoColor;
		_page = new ListPage<UserItem>("Users",
		[
			new Column<UserItem>("ID", 8, u => u.Id.ToString()),
			new Column<UserItem>("Name", 0, u => u.Name),
			new Column<UserItem>("Country", 8, u => u.Country, _ => Ink.Muted),
			new Column<UserItem>("Privileges", 18, u => u.Privileges, u => Role(u.Mask)),
			new Column<UserItem>("Connections", 14, u => u.Connections, _ => Ink.Info)
		], u => u.Id.ToString(), new UsersSource(services, clock),
			user => Detail(user, console, showConsole, options, glyphs), glyphs, colours, clock, post, logger);
	}

	/// <inheritdoc />
	public Visual View => _page.View;

	/// <inheritdoc />
	public void Attach(TerminalApp app)
	{
		_page.Attach(app);
	}

	/// <inheritdoc />
	public void Activate()
	{
		if (_opened) return;

		_opened = true;
		_page.Refresh();
	}

	/// <inheritdoc />
	public void Tick()
	{
		_page.Tick();
	}

	/// <inheritdoc />
	public void Refresh()
	{
		_page.Refresh();
	}

	/// <inheritdoc />
	public void Select(string id)
	{
		_page.Select(id);
	}

	private static Ink Role(ulong mask)
	{
		var privileges = (UserPrivileges)mask;
		return privileges.HasFlag(UserPrivileges.Developer) || privileges.HasFlag(UserPrivileges.Owner)
			? Ink.RoleStaff
			: privileges.HasFlag(UserPrivileges.Player) ? Ink.RolePlayer : Ink.RoleRestricted;
	}

	private static DetailView Detail(UserItem user, ConsoleTab console, Action showConsole, CliOptions options,
		Glyphs glyphs)
	{
		var room = user.Room.Length == 0 ? "-" : user.Room;
		var managed = user.Managed.Length == 0 ? string.Empty : $" ({user.Managed})";
		var fields = new List<(string, string, Ink)>
		{
			("ID", user.Id.ToString(), Ink.Text), ("Name", user.Name, Ink.Text), ("Country", user.Country, Ink.Text),
			("Privileges", $"{user.Privileges} ({user.Mask})", Role(user.Mask)),
			("Connections", user.Connections.Length == 0 ? "offline" : user.Connections, Ink.Info),
			("Room", room + managed, Ink.Text), ("Activity", user.Activity.Length == 0 ? "-" : user.Activity, Ink.Text)
		};
		if (!options.Editable) return new DetailView(fields, []);

		var id = user.Id;
		var actions = new List<DetailAction>
		{
			new("Kick user", () => Run(console, $"users {id} sessions delete"), true),
			user.Silenced
				? new DetailAction("Unsilence", () => Run(console, $"users {id} unsilence"))
				: new DetailAction("Silence…", () => Ask(console, "Silence", id)),
			new("Sessions", () =>
			{
				showConsole();
				return Run(console, $"users {id} sessions list");
			})
		};
		return new DetailView(fields, actions);
	}

	private static Task Run(ConsoleTab console, string line)
	{
		console.RunCommand(line);
		return Task.CompletedTask;
	}

	private static Task Ask(ConsoleTab console, string title, int id)
	{
		Dialogs.Form(title, [("Duration", "30m"), ("Reason", string.Empty)], values =>
		{
			var reason = values[1].Length == 0 ? string.Empty : $" --reason \"{values[1].Replace("\"", "'")}\"";
			console.RunCommand($"users {id} silence --duration {values[0]}{reason}");
		});
		return Task.CompletedTask;
	}
}
