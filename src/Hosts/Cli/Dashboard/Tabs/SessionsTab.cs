using System.Globalization;
using Basil.Application.Users.Objects;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Shell;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>The sessions of the accounts, grouped by the kind of client, with the account and the way to close each.</summary>
internal sealed class SessionsTab : ITabView, ISearchable
{
	private const string Help = """
		With no text, the sessions open now are listed.
		Words match the account name (ignoring case) or its exact id; the sessions
		of the accounts found are listed, ended ones too.
		Example: bob
		""";

	private readonly ListPage<SessionItem> _page;
	private bool _opened;

	/// <summary>Creates the tab.</summary>
	public SessionsTab(IServiceProvider services, UserDetails users, ConsoleTab console, CliOptions options,
		Glyphs glyphs, ShellKit kit, TimeProvider clock, Action<Action> post, ILogger logger)
	{
		_page = new ListPage<SessionItem>("Sessions", Help,
		[
			new Column<SessionItem>("User", 0, s => s.UserName, s => UserInk.Of((UserPrivileges)s.Mask))
			{
				Link = s => users.Of(UserReference.ById(s.UserId))
			},
			new Column<SessionItem>("Last seen", 17, s => s.LastSeen, s => s.Live ? Ink.Success : Ink.Muted),
			new Column<SessionItem>("Since", 17, s => SessionItem.Time(s.Started), _ => Ink.Muted),
			new Column<SessionItem>("Client / IP", 0, s => s.Where) { Weight = 2 }
		], s => s.Id.ToString(CultureInfo.InvariantCulture), new SessionsSource(services, clock),
			session => Detail(session, users, console, options), glyphs, kit, options.NoColor, clock, post, logger);
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
		_page.FocusList();
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
	public void FocusSearch()
	{
		_page.FocusSearch();
	}

	/// <inheritdoc />
	public void ShowHelp()
	{
		_page.ShowHelp();
	}

	private static DetailView Detail(SessionItem session, UserDetails users, ConsoleTab console, CliOptions options)
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
		var actions = new List<DetailAction>
		{
			DetailAction.Open("User", () => users.Of(UserReference.ById(session.UserId)))
		};
		if (options.Editable && session.Live)
			actions.Add(new DetailAction("Close session",
				() => console.RunCommand($"users {session.UserId} sessions {session.Id} delete"),
				DetailActionKind.Destructive));
		return new DetailView(session.Id.ToString(CultureInfo.InvariantCulture), fields, actions);
	}
}
