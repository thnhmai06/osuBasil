using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using Basil.Host.Cli.Dashboard.Tabs;
using Basil.Host.Cli.Shutdown;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard;

/// <summary>
///     The terminal dashboard: ten tabs, a line of key hints under them, and a "too small" screen when the terminal is
///     smaller than the tab in view needs; run on the thread that calls <see cref="Run" />.
/// </summary>
public sealed class DashboardApp
{
	private readonly Glyphs _glyphs;
	private readonly TimeProvider _clock;
	private readonly State<int> _columns = new(120);
	private readonly ConsoleTab _console;
	private readonly State<int> _rows = new(40);
	private readonly ILogger<DashboardApp> _logger;
	private readonly CliOptions _mode;
	private readonly State<string> _flash = new(string.Empty);
	private readonly State<string> _notice = new(string.Empty);
	private readonly Palette _palette;
	private readonly DashboardSampler _sampler;
	private readonly ShutdownController _shutdown;
	private readonly State<int> _selected = new(0);
	private readonly Dictionary<DashboardTab, ITabView> _views = [];
	private readonly IServiceProvider _services;
	private int _lastActive = -1;
	private readonly ThemeStore _themes;
	private TerminalApp? _app;
	private long _flashAt;
	private long _lastSample;
	private int _sampling;
	private TabControl? _tabs;

	/// <summary>Creates the dashboard.</summary>
	public DashboardApp(IServiceProvider services, DashboardModel model, DashboardSampler sampler, CommandRunner runner,
		CommandSuggestions suggestions, ShutdownController shutdown, ThemeStore themes, CliOptions mode,
		TimeProvider clock, ILogger<DashboardApp> logger)
	{
		_sampler = sampler;
		_services = services;
		_shutdown = shutdown;
		_themes = themes;
		_mode = mode;
		_clock = clock;
		_logger = logger;
		_glyphs = new Glyphs(mode.Ascii);
		_palette = new Palette(mode.NoColor);
		_console = new ConsoleTab(model.Log, runner, suggestions, mode, _glyphs, new Palette(mode.NoColor), logger);
		_views[DashboardTab.Users] = new UsersTab(services, _console, () => _selected.Value = (int)DashboardTab.Console,
			mode, _glyphs, clock, action => _app?.Post(action), logger);
		_views[DashboardTab.Lobby] = new LobbyTab(services, _console, name => OpenIn(DashboardTab.Chat, name), mode,
			_glyphs, clock, action => _app?.Post(action), logger);
		_views[DashboardTab.Beatmaps] = new BeatmapsTab(services, _console, mode, _glyphs, clock,
			action => _app?.Post(action), logger);
		_views[DashboardTab.Scores] = new ScoresTab(services, OpenIn, _glyphs, mode, clock,
			action => _app?.Post(action), logger);
		_views[DashboardTab.About] = new AboutTab(services, _glyphs, mode, () => (_columns.Value, _rows.Value), action => _app?.Post(action), logger);
		_views[DashboardTab.Monitor] = new MonitorTab(model, themes, _glyphs, mode, () => (_columns.Value, _rows.Value));
		_views[DashboardTab.Connections] = new ConnectionsTab(model, themes, _glyphs, mode);
		_views[DashboardTab.Chat] = new ChatTab(services, model.Chat, mode, _glyphs, new Palette(mode.NoColor),
			() => (_columns.Value, _rows.Value), logger);
		_views[DashboardTab.Settings] = new SettingsTab(services, _console, mode, _glyphs,
			action => _app?.Post(action), logger);
	}

	private DashboardTab Tab => (DashboardTab)Math.Clamp(_selected.Value, 0, Enum.GetValues<DashboardTab>().Length - 1);

	/// <summary>Builds what the dashboard shows.</summary>
	public Visual Build()
	{
		var pages = Enum.GetValues<DashboardTab>().Select(tab => new TabPage(
			new Markup(Header(tab)),
			new Padder(Content(tab)).Padding(new Thickness(1, 0, 1, 0)).Stretch())).ToArray();
		var strip = (_mode.Ascii ? TabControlStyle.Ascii : TabControlStyle.NoBorder) with { TabPadding = new Thickness(0, 0, 1, 0) };
		_tabs = new TabControl(pages).Style(strip).SelectedIndex(_selected);
		var status = new TextBlock(() => _notice.Value.Length > 0 ? _notice.Value : _flash.Value.Length > 0 ? _flash.Value : Hints());
		var main = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Star() }, new RowDefinition { Height = GridLength.Auto })
			.Cell(_tabs, 0, 0)
			.Cell(status, 1, 0);
		var small = new Center(new Group("Terminal too small").Style(GroupStyle.Rounded).Padding(1)
				.Content(new VStack(new Markup(() => SmallLine(0)), new Markup(() => SmallLine(1)),
					new TextBlock("Enlarge the window or zoom out."))));
		return new Padder(() => TooSmall() ? small : main).Stretch();
	}

	/// <summary>Runs the dashboard until the server stops.</summary>
	/// <param name="terminal">The terminal to run on.</param>
	/// <param name="stopped">Tells that the server stopped.</param>
	public void Run(TerminalInstance terminal, CancellationToken stopped)
	{
		var root = Build();
		root.SetStyle(Theme.Key, _themes.Theme);
		_themes.Changed += theme => _app?.Post(() => _app.Root.SetStyle(Theme.Key, theme));
		_shutdown.Message += OnShutdownMessage;
		terminal.Run(root, context =>
		{
			if (_app is null)
			{
				_app = context.App;
				AddKeys(_app);
				_console.Attach(_app);
				foreach (var view in _views.Values) view.Attach(_app);
			}

			ActiveView()?.Tick();
			if (_lastActive != _selected.Value)
			{
				_lastActive = _selected.Value;
				ActiveView()?.Activate();
				if (Tab == DashboardTab.Console) _console.Activate();
			}

			_columns.Value = terminal.Size.Columns;
			_rows.Value = terminal.Size.Rows;
			_console.Pump();
			var now = _clock.GetTimestamp();
			if (_flash.Value.Length > 0 && _clock.GetElapsedTime(_flashAt, now) >= TimeSpan.FromSeconds(2))
				_flash.Value = string.Empty;

			if (_clock.GetElapsedTime(_lastSample, now) >= TimeSpan.FromSeconds(1))
			{
				_lastSample = now;
				_shutdown.Tick();
				StartSample();
			}

			return stopped.IsCancellationRequested ? TerminalLoopResult.Stop : TerminalLoopResult.Continue;
		}, new TerminalRunOptions
		{
			ExitGesture = null, LoopMode = TerminalLoopMode.Polling, UpdateWaitDuration = TimeSpan.FromMilliseconds(100)
		});
		_shutdown.Message -= OnShutdownMessage;
		_console.Stop();
	}

	private string Header(DashboardTab tab)
	{
		return $"{_palette.Wrap(Ink.Muted, $"F{(int)tab + 1}")} {tab}";
	}

	private Visual Content(DashboardTab tab)
	{
		return tab == DashboardTab.Console ? _console.View
			: _views.TryGetValue(tab, out var view) ? view.View : PlaceholderTab.Create(tab.ToString());
	}

	private void OpenIn(DashboardTab tab, string id)
	{
		_selected.Value = (int)tab;
		if (_views.GetValueOrDefault(tab) is ISelectable view) view.Select(id);
	}

	private ITabView? ActiveView()
	{
		return _views.GetValueOrDefault(Tab);
	}

	private bool TooSmall()
	{
		return Layout.Of(_columns.Value, _rows.Value, Tab) == Breakpoint.TooSmall;
	}

	private string SmallLine(int line)
	{
		var (needWidth, needHeight) = Layout.Needed(Tab);
		var width = _palette.Wrap(_columns.Value < needWidth ? Ink.Error : Ink.Success, needWidth.ToString());
		var height = _palette.Wrap(_rows.Value < needHeight ? Ink.Error : Ink.Success, needHeight.ToString());
		return line == 0
			? $"Current : {_columns.Value} × {_rows.Value}"
			: $"Needed  : {width} × {height}  ({Tab})";
	}

	private string Hints()
	{
		var dot = $" {_glyphs.Dot} ";
		string[] hints = Tab switch
		{
			DashboardTab.Chat => _mode.Editable
				? ["Enter send", "Tab pane", "Ctrl+B members", "Esc/Alt+← back", "Ctrl+End follow"]
				: ["Tab pane", "Ctrl+B members", "Esc/Alt+← back", "Ctrl+End follow"],
			DashboardTab.Settings => ["←→ category", "↓/Enter fields", "↑/Esc back", "Enter save"],
			DashboardTab.Console => _mode.Editable
				? ["Enter run", "Tab/↑↓ complete", "Esc close", "Ctrl+End follow"]
				: ["Ctrl+End follow"],
			DashboardTab.Monitor or DashboardTab.Connections or DashboardTab.About => ["Ctrl+R refresh"],
			_ => ["↑↓ select", "Enter open", "/ search", "? help", "Ctrl+R refresh"]
		};
		return " " + string.Join(dot, _mode.Editable ? hints : [.. hints, "view only"]);
	}

	private void StartSample()
	{
		if (Interlocked.Exchange(ref _sampling, 1) == 1) return;

		_ = Task.Run(() =>
		{
			try
			{
				_sampler.Sample();
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "The dashboard could not read the server");
			}
			finally
			{
				Interlocked.Exchange(ref _sampling, 0);
			}
		});
	}

	private void AddKeys(TerminalApp app)
	{
		for (var i = 0; i < Enum.GetValues<DashboardTab>().Length; i++)
		{
			var index = i;
			app.AddGlobalCommand(new Command
			{
				Id = $"tab.{(DashboardTab)index}", LabelMarkup = ((DashboardTab)index).ToString(),
				Gesture = new KeyGesture(TerminalKey.F1 + index), Execute = _ => _selected.Value = index
			});
			app.AddGlobalCommand(new Command
			{
				Id = $"tab.alt.{(DashboardTab)index}", LabelMarkup = ((DashboardTab)index).ToString(),
				Gesture = new KeyGesture((char)('0' + (index + 1) % 10), TerminalModifiers.Alt),
				Execute = _ => _selected.Value = index
			});
		}

		app.AddGlobalCommand(new Command
		{
			Id = "interrupt", LabelMarkup = "Interrupt", Gesture = new KeyGesture(TerminalChar.CtrlC, TerminalModifiers.Ctrl),
			Execute = _ => _shutdown.Interrupt(!string.IsNullOrEmpty(_console.Prompt?.Text))
		});
		app.AddGlobalCommand(new Command
		{
			Id = "decline", LabelMarkup = "Decline", Gesture = new KeyGesture(TerminalKey.Escape),
			CanExecute = _ => _console.Asking, Execute = _ => _console.Decline()
		});
		var refresh = new Command
		{
			Id = "refresh", LabelMarkup = "Refresh", Gesture = new KeyGesture(TerminalChar.CtrlR, TerminalModifiers.Ctrl),
			Execute = _ =>
			{
				ActiveView()?.Refresh();
				_flashAt = _clock.GetTimestamp();
				_flash.Value = $" Refreshed {_clock.GetLocalNow():HH:mm:ss}";
			}
		};
		app.AddGlobalCommand(refresh);
		// The command box takes Ctrl+R for itself, so it needs the command too.
		_console.Prompt?.AddCommand(refresh);
		foreach (var (id, key, step) in new[] { ("tab.previous", TerminalKey.Left, -1), ("tab.next", TerminalKey.Right, 1) })
			app.AddGlobalCommand(new Command
			{
				Id = id, LabelMarkup = id, Gesture = new KeyGesture(key, TerminalModifiers.Ctrl),
				CanExecute = _ => app.FocusedElement is not (TextBox or PromptEditor or TextArea),
				Execute = _ => _selected.Value = Math.Clamp(_selected.Value + step, 0, Enum.GetValues<DashboardTab>().Length - 1)
			});
		app.AddGlobalCommand(new Command
		{
			Id = "jump.end", LabelMarkup = "Jump to End",
			Gesture = new KeyGesture(TerminalKey.End, TerminalModifiers.Ctrl), Execute = _ => { if (ActiveView() is IJumpable view) view.JumpToEnd(); else _console.JumpToEnd(); }
		});
	}

	private void OnShutdownMessage(ShutdownMessage message)
	{
		_app?.Post(() =>
		{
			switch (message.Kind)
			{
				case ShutdownMessageKind.ClearLine:
					_console.ClearPrompt();
					break;
				case ShutdownMessageKind.Warning or ShutdownMessageKind.Progress:
					_notice.Value = message.Text;
					break;
				case ShutdownMessageKind.Clear:
					_notice.Value = string.Empty;
					break;
			}
		});
	}
}