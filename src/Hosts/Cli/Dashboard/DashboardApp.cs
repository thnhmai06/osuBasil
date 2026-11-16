using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using Basil.Host.Cli.Dashboard.Shell.Toasts;
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
	private readonly Dialogs _dialogs = new();
	private readonly ToastTray _toasts;
	private readonly Palette _palette;
	private readonly DashboardSampler _sampler;
	private readonly ShutdownController _shutdown;
	private readonly State<int> _selected = new(0);
	private readonly Dictionary<DashboardTab, ITabView> _views = [];
	private readonly IServiceProvider _services;
	private int _lastActive = -1;
	private readonly ThemeStore _themes;
	private TerminalApp? _app;
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
		_toasts = new ToastTray(_glyphs, _palette, clock, Post);
		var kit = new ShellKit(_dialogs, _toasts);
		_console = new ConsoleTab(model.Log, runner, suggestions, mode, _glyphs, new Palette(mode.NoColor), _dialogs, clock,
			logger);
		var users = new UserDetails(services, _console, mode, _dialogs, _glyphs, clock, name => OpenIn(DashboardTab.Chat, name));
		var beatmaps = new BeatmapDetails(services, _console, mode);
		_views[DashboardTab.Users] = new UsersTab(services, users, mode, _glyphs, kit, clock, Post, logger);
		_views[DashboardTab.Lobby] = new LobbyTab(services, users, _console, name => OpenIn(DashboardTab.Chat, name), mode,
			_glyphs, kit, clock, Post, logger);
		_views[DashboardTab.Beatmaps] = new BeatmapsTab(services, beatmaps, mode, _glyphs, kit, clock, Post, logger);
		_views[DashboardTab.Scores] = new ScoresTab(services, users, beatmaps, _glyphs, kit, mode, clock, Post, logger);
		_views[DashboardTab.About] = new AboutTab(services, _glyphs, mode, () => (_columns.Value, _rows.Value), Post, logger);
		_views[DashboardTab.Monitor] = new MonitorTab(model, themes, _glyphs, mode, () => (_columns.Value, _rows.Value), clock);
		_views[DashboardTab.Sessions] = new SessionsTab(services, users, mode, _glyphs, kit, clock, Post, logger);
		_views[DashboardTab.Chat] = new ChatTab(services, users, model.Chat, mode, _glyphs, kit,
			new Palette(mode.NoColor), () => (_columns.Value, _rows.Value), clock, Post, logger);
		_views[DashboardTab.Settings] = new SettingsTab(services, _console, mode, _glyphs, kit, clock, Post, logger);
	}

	private void Post(Action action)
	{
		_app?.Post(action);
	}

	private DashboardTab Tab => (DashboardTab)Math.Clamp(_selected.Value, 0, Enum.GetValues<DashboardTab>().Length - 1);

	/// <summary>Builds what the dashboard shows.</summary>
	public Visual Build()
	{
		var pages = Enum.GetValues<DashboardTab>().Select(tab => new TabPage(
			new Markup(Header(tab)),
			new Padder(WithRefresh(tab)).Padding(new Thickness(1, 0, 1, 0)).Stretch())).ToArray();
		var strip = (_mode.Ascii ? TabControlStyle.Ascii : TabControlStyle.NoBorder) with { TabPadding = new Thickness(0, 0, 1, 0) };
		_tabs = new TabControl(pages).Style(strip).SelectedIndex(_selected);
		var status = new ComputedVisual(StatusBar);
		var main = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Star() }, new RowDefinition { Height = GridLength.Auto })
			.Cell(_toasts.Over(_tabs), 0, 0)
			.Cell(status, 1, 0);
		var small = new Center(new Group("Terminal too small").Style(GroupStyle.Rounded).Padding(1)
				.Content(new VStack(new Markup(() => SmallLine(0)), new Markup(() => SmallLine(1)),
					new TextBlock("Enlarge the window or zoom out."))));
		return _dialogs.Dim(new Padder(() => TooSmall() ? small : main).Stretch());
	}

	/// <summary>Runs the dashboard until the server stops.</summary>
	/// <param name="terminal">The terminal to run on.</param>
	/// <param name="stopped">Tells that the server stopped.</param>
	public void Run(TerminalInstance terminal, CancellationToken stopped)
	{
		var root = Build();
		ButtonHover.Apply(root, _themes.Theme);
		_themes.Changed += theme => _app?.Post(() => ButtonHover.Apply(_app.Root, theme));
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
			_dialogs.Settle();
			_toasts.Tick();
			var now = _clock.GetTimestamp();

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

	// The key reminders of the tab in view; the ones with an action can be clicked.
	private Visual StatusBar()
	{
		var items = new List<(string Text, Action? Click)>();
		var searchable = ActiveView() as ISearchable;
		switch (Tab)
		{
			case DashboardTab.Chat:
				if (_mode.Editable) items.Add(("Enter send", null));
				items.AddRange([("Tab pane", null), ("Ctrl+B members", null), ("Esc/Alt+← back", null)]);
				break;
			case DashboardTab.Settings:
				items.AddRange([("←→ category", null), ("↓ search", null), ("↑↓ select", null), ("←→ fold", null), ("Enter save", null), ("Esc back", null)]);
				break;
			case DashboardTab.Console:
				if (_mode.Editable) items.AddRange([("Enter run", null), ("Tab/↑↓ complete", null), ("Esc close", null)]);
				break;
			default:
				if (searchable is not null)
					items.AddRange([("↑↓ select", null), ("Enter open", null), ("/ search", searchable.FocusSearch),
						("? help", searchable.ShowHelp)]);
				break;
		}

		if (!_mode.Editable) items.Add(("view only", null));
		var bar = new HStack().Spacing(0);
		bar.Add(new HoverLabel(" ", null));
		for (var i = 0; i < items.Count; i++)
		{
			if (i > 0) bar.Add(new HoverLabel($" {_glyphs.Dot} ", null));
			bar.Add(new HoverLabel(items[i].Text, items[i].Click));
		}

		return bar;
	}

	// The page of a tab, with the Refresh button on the right of its top border for the tabs that load their data (the chat updates itself).
	private Visual WithRefresh(DashboardTab tab)
	{
		var content = Content(tab);
		if (!_views.TryGetValue(tab, out var view)) return content;

		// A page of plain content scrolls with the accelerated wheel; the lists and logs do it themselves.
		if (view is not ISearchable && tab != DashboardTab.Chat)
			WheelScroll.Accelerate(content,
				() => content.EnumerateVisualsDepthFirst().OfType<ScrollViewer>().FirstOrDefault(), _clock);

		var button = new Button($"[{_glyphs.Refresh} Refresh]").Click(RefreshView).IsTabStop(false)
			.HorizontalAlignment(Align.End).VerticalAlignment(Align.Start).Margin(new Thickness(0, 0, 2, 0));
		return Refreshes(tab) ? new ZStack(content, button) : content;
	}

	// The chat and the Monitor update themselves and the About page holds nothing to reload, so they have no Refresh.
	private static bool Refreshes(DashboardTab tab)
	{
		return tab is not (DashboardTab.Chat or DashboardTab.Monitor or DashboardTab.About);
	}

	private void RefreshView()
	{
		ActiveView()?.Refresh();
		_toasts.Show("Refreshed");
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
			Id = "toast.dismiss", LabelMarkup = "Dismiss", Gesture = new KeyGesture(TerminalKey.Escape),
			CanExecute = _ => _toasts.Any, Execute = _ => _toasts.DismissAll()
		});
		app.AddGlobalCommand(new Command
		{
			Id = "decline", LabelMarkup = "Decline", Gesture = new KeyGesture(TerminalKey.Escape),
			CanExecute = _ => _console.Asking, Execute = _ => _console.Decline()
		});
		var refresh = new Command
		{
			Id = "refresh", LabelMarkup = "Refresh", Gesture = new KeyGesture(TerminalChar.CtrlR, TerminalModifiers.Ctrl),
			CanExecute = _ => Refreshes(Tab), Execute = _ => RefreshView()
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
				case ShutdownMessageKind.Warning:
					_toasts.Show(message.Text, ToastKind.Warning);
					break;
				case ShutdownMessageKind.Progress:
					_toasts.Show(message.Text);
					break;
			}
		});
	}
}