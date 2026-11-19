using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed record UserItem(
	int Id,
	string Name,
	string Country,
	string Privileges,
	ulong Mask,
	string Connections,
	string Room,
	string Managed,
	string Activity,
	bool Silenced,
	ulong Granted);

internal sealed class UsersSource(IServiceProvider services, TimeProvider clock) : IListSource<UserItem>
{
	public async Task<IReadOnlyList<SectionData<UserItem>>> LoadAsync(string query, int limit, CancellationToken ct)
	{
		var registry = services.GetRequiredService<UserRegistry>();
		var lobby = services.GetRequiredService<Lobby>();
		// The table hides deleted accounts, as the delete dialog promises, unless the search asks for them.
		var parsed = UserQuery.Parse(query);
		if (!query.Contains("deleted", StringComparison.OrdinalIgnoreCase)) parsed = parsed with { Deleted = false };
		var now = clock.GetUtcNow();
		var online = registry.Online.Values.Where(user => user.Record.Id != BasilBot.Id)
			.Where(user => Matches(parsed, user.Record.Id, user.Record.Value.Name,
				user.Record.Value.Country.ToAcronym()))
			.OrderBy(user => user.Record.Id)
			.Select(user => Item(lobby, user.Record, user, now)).ToList();
		var onlineIds = online.Select(item => item.Id).ToHashSet();
		var actor = await registry.OperateAsync(ct);
		var found = await services.GetRequiredService<ISender>()
			.Send(new SearchUsers(actor, parsed, new PageRequest(0, limit)), ct);
		var offline = found.Items.Where(record => !onlineIds.Contains(record.Id) && record.Id != BasilBot.Id)
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

	internal static UserItem Item(Lobby lobby, UserRecord record, User? user,
		DateTimeOffset now)
	{
		var mask = user is null ? (ulong)record.Value.Privileges : (ulong)user.EffectivePrivileges(now);
		var room = user is null
			? string.Empty
			: lobby.Rooms.Values.FirstOrDefault(r =>
				r.Slots.Any(slot => slot.Player is not null && slot.Player.Equals(user.Record)))?.Id is { } id
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
			room, managed, user?.Stable?.Status.Activity.ToString() ?? string.Empty, record.Value.IsSilenced(now),
			(ulong)record.Value.Privileges);
	}
}

internal sealed class UsersTab : ITabView, ISelectable, ISearchable
{
	private const string Help = """
	                            Words match the name (ignoring case) or the exact id.
	                            country=usvn     only these countries (two-letter codes, joined)
	                            privilege=4      only accounts granted all these privilege flags (number)
	                            deleted=1|0|*    only deleted, only existing (the default), or both (owner only)
	                            Example: country=vn privilege=4 bob
	                            """;

	private readonly ListPage<UserItem> _page;
	private bool _opened;

	public UsersTab(IServiceProvider services, UserDetails users, CliOptions options, Glyphs glyphs, ShellKit kit,
		TimeProvider clock, Action<Action> post, ILogger logger)
	{
		var colours = options.NoColor;
		_page = new ListPage<UserItem>("Users", Help,
			[
				new Column<UserItem>("ID", 8, u => u.Id.ToString()) { Right = true },
				new Column<UserItem>("FullName", 16, u => u.Name, u => Role(u.Mask)),
				new Column<UserItem>("Country", 8, u => u.Country, _ => Ink.Muted),
				new Column<UserItem>("Privileges", 0, u => u.Privileges, u => Role(u.Mask)),
				new Column<UserItem>("Connections", 11, u => u.Connections, _ => Ink.Info)
			], u => u.Id.ToString(), new UsersSource(services, clock),
			users.Of, glyphs, kit, colours, clock, post, logger);
	}

	public void FocusSearch()
	{
		_page.FocusSearch();
	}

	public void ShowHelp()
	{
		_page.ShowHelp();
	}

	public void Select(string id)
	{
		_page.Select(id);
	}

	public Visual View => _page.View;

	public void Attach(TerminalApp app)
	{
		_page.Attach(app);
	}

	public void Activate()
	{
		_page.FocusList();
		if (_opened) return;

		_opened = true;
		_page.Refresh();
	}

	public void Tick()
	{
		_page.Tick();
	}

	public void Refresh()
	{
		_page.Refresh();
	}

	private static Ink Role(ulong mask)
	{
		return UserInk.Of((UserPrivileges)mask);
	}
}