using System.Globalization;
using Basil.Application.Users.Objects;
using Basil.Host.Cli.Dashboard.Shell;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>The sessions open now, grouped by the kind of client, with the account and the way to close each.</summary>
internal sealed class SessionsTab : ITabView, ISearchable
{
	private const string Help = """
		Words match the account name or its exact id, the address, the client or the kind.
		The history of one account is in its detail, under Sessions…
		Example: bob
		""";

	private readonly ListPage<SessionItem> _page;
	private bool _opened;

	/// <summary>Creates the tab.</summary>
	public SessionsTab(IServiceProvider services, UserDetails users, CliOptions options, Glyphs glyphs, ShellKit kit,
		TimeProvider clock, Action<Action> post, ILogger logger)
	{
		_page = new ListPage<SessionItem>("Sessions", Help,
			SessionColumns.Of(s => (s.UserName, () => users.OfAsync(UserReference.ById(s.UserId)))),
			s => s.Id.ToString(CultureInfo.InvariantCulture), new SessionsSource(services, clock), users.Of, glyphs, kit,
			options.NoColor, clock, post, logger);
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
}
