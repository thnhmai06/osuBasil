using System.Text;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Tabs;
using Basil.Host.Cli.Shutdown;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Text;

namespace Basil.Host.Cli.Dashboard;

/// <summary>Paints a <see cref="CanvasContext" /> as a dashboard surface, mapping tones to the colours of the theme.</summary>
internal sealed class CanvasSurface(CanvasContext context, Theme theme) : IPaintSurface
{
	private static readonly Color Low = Colors.TerminalGreen;

	/// <inheritdoc />
	public int Width => context.Size.Width;

	/// <inheritdoc />
	public int Height => context.Size.Height;

	/// <inheritdoc />
	public void Put(int x, int y, char character, Basil.Host.Cli.Dashboard.Tone tone, double heat = 0)
	{
		if (x < 0 || y < 0 || x >= Width || y >= Height) return;

		context.WriteText(x, y, character.ToString(), StyleOf(tone, heat));
	}

	private Style StyleOf(Basil.Host.Cli.Dashboard.Tone tone, double heat)
	{
		var foreground = theme.Foreground ?? Colors.TerminalWhite;
		return tone switch
		{
			Basil.Host.Cli.Dashboard.Tone.Muted => Style.None.WithForeground(theme.Muted ?? Colors.TerminalBrightBlack),
			Basil.Host.Cli.Dashboard.Tone.Accent => Style.None.WithForeground(theme.Accent ?? theme.Primary ?? Colors.TerminalCyan),
			Basil.Host.Cli.Dashboard.Tone.Good => Style.None.WithForeground(theme.Success ?? Colors.TerminalGreen),
			Basil.Host.Cli.Dashboard.Tone.Warn => Style.None.WithForeground(theme.Warning ?? Colors.TerminalYellow),
			Basil.Host.Cli.Dashboard.Tone.Bad => Style.None.WithForeground(theme.Error ?? Colors.TerminalRed),
			Basil.Host.Cli.Dashboard.Tone.Selected => Style.None.WithForeground(foreground).WithBackground(theme.Selection ?? Colors.TerminalBlue),
			Basil.Host.Cli.Dashboard.Tone.Heat => Style.None.WithForeground(Gradient(heat)),
			_ => Style.None.WithForeground(foreground)
		};
	}

	private Color Gradient(double heat)
	{
		var good = theme.Success ?? Low;
		var warn = theme.Warning ?? Colors.TerminalYellow;
		var bad = theme.Error ?? Colors.TerminalRed;
		var value = (float)Math.Clamp(heat, 0, 1);
		return value < 0.5f
			? good.Mix(warn, value * 2, ColorMixSpace.Oklab)
			: warn.Mix(bad, (value - 0.5f) * 2, ColorMixSpace.Oklab);
	}
}

/// <summary>
///     The terminal dashboard: the tab in view painted on a canvas, the output of the command box, and the command box
///     itself, run on the thread that calls <see cref="Run" />.
/// </summary>
public sealed class DashboardApp
{
	private readonly DashboardModel _model;
	private readonly DashboardRenderer _renderer;
	private readonly DashboardSampler _sampler;
	private readonly CommandRunner _runner;
	private readonly CommandSuggestions _suggestions;
	private readonly ShutdownController _shutdown;
	private readonly ThemeStore _themes;
	private readonly CliMode _mode;
	private readonly TimeProvider _clock;
	private readonly ILogger<DashboardApp> _logger;
	private readonly PromptEditorHistory _history = new();
	private readonly CancellationTokenSource _cancel = new();
	private TaskCompletionSource<bool>? _confirmation;
	private PromptEditor? _prompt;
	private TerminalApp? _app;
	private long _lastSample;

	/// <summary>Creates the dashboard.</summary>
	public DashboardApp(DashboardModel model, DashboardRenderer renderer, DashboardSampler sampler, CommandRunner runner,
		CommandSuggestions suggestions, ShutdownController shutdown, ThemeStore themes, CliMode mode, TimeProvider clock,
		ILogger<DashboardApp> logger)
	{
		_model = model;
		_renderer = renderer;
		_sampler = sampler;
		_runner = runner;
		_suggestions = suggestions;
		_shutdown = shutdown;
		_themes = themes;
		_mode = mode;
		_clock = clock;
		_logger = logger;
	}

	/// <summary>Builds what the dashboard shows.</summary>
	public Visual Build()
	{
		var canvas = new Canvas().Painter(context => _renderer.Paint(new CanvasSurface(context, _themes.Theme), _mode == CliMode.Full))
			.UseFinePixels(true);
		if (_mode != CliMode.Full) return canvas;

		_prompt = new PromptEditor()
			.LineMode(PromptEditorLineMode.SingleLine)
			.PromptMarkup("[primary]❯[/] ")
			.Placeholder("Type a command (Tab completes, help lists them)")
			.History(_history)
			.CompletionHandler(Complete)
			.Highlighter(Highlight)
			.Accepted(OnAccepted);
		var grid = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star(1) })
			.Rows(new RowDefinition { Height = GridLength.Star(1) }, new RowDefinition { Height = GridLength.Auto })
			.Cell(canvas, row: 0, column: 0)
			.Cell(_prompt, row: 1, column: 0);
		return grid;
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
		_sampler.Sample();
		terminal.Run(root, context =>
		{
			if (_app is null)
			{
				_app = context.App;
				AddKeys(_app);
				if (_prompt is not null) _app.Focus(_prompt);
			}

			var now = _clock.GetTimestamp();
			if (_clock.GetElapsedTime(_lastSample, now) >= TimeSpan.FromSeconds(1))
			{
				_lastSample = now;
				try
				{
					_sampler.Sample();
					_shutdown.Tick();
				}
				catch (Exception exception)
				{
					_logger.LogWarning(exception, "The dashboard could not read the server");
				}

				_app.RequestFullRender();
			}

			return stopped.IsCancellationRequested ? TerminalLoopResult.Stop : TerminalLoopResult.Continue;
		}, new TerminalRunOptions
		{
			ExitGesture = null, LoopMode = TerminalLoopMode.Polling, UpdateWaitDuration = TimeSpan.FromMilliseconds(100)
		});
		_shutdown.Message -= OnShutdownMessage;
		_cancel.Cancel();
	}

	private void AddKeys(TerminalApp app)
	{
		for (var i = 0; i < 6; i++)
		{
			var tab = (DashboardTab)i;
			app.AddGlobalCommand(new Command
			{
				Id = $"tab.{tab}", LabelMarkup = tab.ToString(), Gesture = new KeyGesture(TerminalKey.F1 + i, TerminalModifiers.None),
				Execute = _ =>
				{
					_model.Tab = tab;
					_model.Selected = 0;
					app.RequestFullRender();
				}
			});
		}

		app.AddGlobalCommand(new Command
		{
			Id = "interrupt", LabelMarkup = "Interrupt", Gesture = new KeyGesture('c', TerminalModifiers.Ctrl),
			Execute = _ => _shutdown.Interrupt(!string.IsNullOrEmpty(_prompt?.Text))
		});
		app.AddGlobalCommand(new Command
		{
			Id = "select.up", LabelMarkup = "Select up", Gesture = new KeyGesture(TerminalKey.Up, TerminalModifiers.Alt),
			Execute = _ => Move(-1)
		});
		app.AddGlobalCommand(new Command
		{
			Id = "select.down", LabelMarkup = "Select down", Gesture = new KeyGesture(TerminalKey.Down, TerminalModifiers.Alt),
			Execute = _ => Move(1)
		});
		app.AddGlobalCommand(new Command
		{
			Id = "select.fill", LabelMarkup = "Use the selected id", Gesture = new KeyGesture(TerminalKey.Enter, TerminalModifiers.Alt),
			Execute = _ => FillSelectedId()
		});
		app.AddGlobalCommand(new Command
		{
			Id = "log.pause", LabelMarkup = "Pause the log", Gesture = new KeyGesture(TerminalKey.PageUp, TerminalModifiers.None),
			Execute = _ => PauseLog(true)
		});
		app.AddGlobalCommand(new Command
		{
			Id = "log.follow", LabelMarkup = "Follow the log", Gesture = new KeyGesture(TerminalKey.PageDown, TerminalModifiers.None),
			Execute = _ => PauseLog(false)
		});
	}

	private void PauseLog(bool pause)
	{
		_model.LogPaused = pause;
		_model.LogFrozen = pause ? _model.Log.Lines() : null;
		_app?.RequestFullRender();
	}

	private void Move(int delta)
	{
		var count = _model.Tab == DashboardTab.Users ? _model.Users.Count : _model.RoomList.Count;
		_model.Selected = Math.Clamp(_model.Selected + delta, 0, Math.Max(count - 1, 0));
		_app?.RequestFullRender();
	}

	private void FillSelectedId()
	{
		if (_prompt is null) return;

		var id = _model.Tab == DashboardTab.Users && _model.Selected < _model.Users.Count
			? _model.Users[_model.Selected].Id
			: _model.Tab == DashboardTab.Rooms && _model.Selected < _model.RoomList.Count
				? _model.RoomList[_model.Selected].Id
				: (int?)null;
		if (id is null) return;

		var resource = _model.Tab == DashboardTab.Users ? "users" : "rooms";
		_prompt.Text = _prompt.Text.Length == 0 ? $"{resource} {id} " : $"{_prompt.Text.TrimEnd()} {id} ";
		_app?.Focus(_prompt);
	}

	private void OnShutdownMessage(ShutdownMessage message)
	{
		_app?.Post(() =>
		{
			switch (message.Kind)
			{
				case ShutdownMessageKind.ClearLine:
					if (_prompt is not null) _prompt.Text = string.Empty;
					break;
				case ShutdownMessageKind.Warning:
					_model.Notice = message.Text;
					break;
				case ShutdownMessageKind.Progress:
					_model.Notice = string.Empty;
					_model.ShutdownStatus = message.Text;
					break;
				case ShutdownMessageKind.Clear:
					_model.Notice = string.Empty;
					break;
			}

			_app.RequestFullRender();
		});
	}

	private void OnAccepted()
	{
		var line = _prompt!.Text.Trim();
		_prompt.Text = string.Empty;
		if (line.Length == 0) return;

		if (_confirmation is { } pending)
		{
			_confirmation = null;
			_model.Notice = string.Empty;
			pending.TrySetResult(line.Equals("y", StringComparison.OrdinalIgnoreCase) || line.Equals("yes", StringComparison.OrdinalIgnoreCase));
			return;
		}

		_history.Add(Redactor.Redact(_runner.Catalog, line));
		_model.AddOutput(new OutputLine(OutputKind.Text, "❯ " + Redactor.Redact(_runner.Catalog, line)));
		_ = RunAsync(line);
	}

	private async Task RunAsync(string line)
	{
		var args = CommandCatalog.Split(line);
		var output = new ModelOutput(_model, _app);
		try
		{
			var session = new CommandSession(true, AskAsync);
			await _runner.RunAsync(args, output, true, session, _cancel.Token);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.LogError(exception, "A console command failed");
			output.Error("internal", "The command failed; see the log.");
		}
	}

	private Task<bool> AskAsync(string question, CancellationToken cancellationToken)
	{
		var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_confirmation = source;
		_model.Notice = question + " (y/N)";
		cancellationToken.Register(() => source.TrySetCanceled());
		return source.Task;
	}

	private PromptEditorCompletion Complete(in PromptEditorCompletionRequest request)
	{
		var builder = new StringBuilder(request.CaretIndex);
		for (var i = 0; i < request.CaretIndex && i < request.Snapshot.Length; i++) builder.Append(request.Snapshot[i]);
		var (words, start, length) = _suggestions.Suggest(builder.ToString());
		return words.Count == 0
			? new PromptEditorCompletion(false, null, 0, 0)
			: new PromptEditorCompletion(true, words, start, length);
	}

	private void Highlight(in PromptEditorHighlightRequest request, List<StyledRun> runs)
	{
		var text = new StringBuilder(request.Snapshot.Length);
		for (var i = 0; i < request.Snapshot.Length; i++) text.Append(request.Snapshot[i]);
		var theme = request.Theme;
		foreach (var token in _suggestions.Highlight(text.ToString()))
		{
			var color = token.Kind switch
			{
				TokenKind.Command => theme.Accent ?? Colors.TerminalCyan,
				TokenKind.Subcommand => theme.Primary ?? Colors.TerminalBlue,
				TokenKind.Value => theme.Warning ?? Colors.TerminalYellow,
				TokenKind.Option => theme.Success ?? Colors.TerminalGreen,
				TokenKind.OptionValue => theme.Foreground ?? Colors.TerminalWhite,
				_ => theme.Error ?? Colors.TerminalRed
			};
			runs.Add(new StyledRun(token.Start, token.Length, Style.None.WithForeground(color)));
		}
	}

	private sealed class ModelOutput(DashboardModel model, TerminalApp? app) : ICommandOutput
	{
		public void Write(OutputLine line)
		{
			foreach (var part in line.Text.Split('\n')) model.AddOutput(line with { Text = part });
			app?.Post(app.RequestFullRender);
		}
	}
}
