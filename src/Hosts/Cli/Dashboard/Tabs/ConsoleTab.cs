using System.Text;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Text;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>
///     The console: the log of the server written straight on the screen, and, at the full level, the command box under
///     it whose output goes into the same log.
/// </summary>
internal sealed class ConsoleTab
{
	private readonly CancellationTokenSource _cancel = new();
	private readonly Glyphs _glyphs;
	private readonly PromptEditorHistory _history = new();
	private readonly LogBuffer _logBuffer;
	private readonly ILogger _logger;
	private readonly LogControl _log;
	private readonly CommandRunner _runner;
	private readonly CommandSuggestions _suggestions;
	private readonly Palette _tokens;
	private TerminalApp? _app;
	private long _seen;

	/// <summary>Creates the tab.</summary>
	public ConsoleTab(LogBuffer logBuffer, CommandRunner runner, CommandSuggestions suggestions, CliOptions options,
		Glyphs glyphs, Palette tokens, ILogger logger)
	{
		_logBuffer = logBuffer;
		_runner = runner;
		_suggestions = suggestions;
		_glyphs = glyphs;
		_tokens = tokens;
		_logger = logger;
		_log = new LogControl { MaxCapacity = 500 }.WrapText(true);
		if (!options.Editable)
		{
			View = _log;
			return;
		}

		Prompt = new PromptEditor()
			.LineMode(PromptEditorLineMode.SingleLine)
			.PromptMarkup($"[cyan]{XenoAtom.Ansi.AnsiMarkup.Escape(glyphs.Prompt)}[/] ")
			.Placeholder("Type a command (Tab completes, help lists them)")
			.History(_history)
			.CompletionHandler(Complete)
			.Highlighter(Highlight)
			.Accepted(OnAccepted);
		View = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Star() }, new RowDefinition { Height = GridLength.Auto })
			.Cell(_log, 0, 0)
			.Cell(Prompt, 1, 0);
	}

	/// <summary>Gets what the tab shows.</summary>
	public Visual View { get; }

	/// <summary>Gets the command box, or <see langword="null" /> when the console cannot change the server.</summary>
	public PromptEditor? Prompt { get; }

	/// <summary>Connects the tab to the running application.</summary>
	public void Attach(TerminalApp app)
	{
		_app = app;
		if (Prompt is not null) app.Focus(Prompt);
	}

	/// <summary>Stops the commands the tab still runs.</summary>
	public void Stop()
	{
		_cancel.Cancel();
	}

	/// <summary>Moves the lines logged since the last call into the log control.</summary>
	public void Pump()
	{
		var fresh = _logBuffer.Since(_seen, out var total);
		foreach (var line in fresh.Where(line => line.Level >= LogLevel.Information)) _log.AppendMarkupLine(Format(line));
		_seen = total;
	}

	/// <summary>Follows the newest line again.</summary>
	public void JumpToEnd()
	{
		_log.ScrollToTail();
	}

	/// <summary>Clears the command box.</summary>
	public void ClearPrompt()
	{
		Prompt?.Text = string.Empty;
	}

	private string Format(LogLine line)
	{
		var (name, token) = line.Level switch
		{
			LogLevel.Trace => ("TRC", Ink.Muted),
			LogLevel.Debug => ("DBG", Ink.Muted),
			LogLevel.Information => ("INF", Ink.Info),
			LogLevel.Warning => ("WRN", Ink.Warning),
			LogLevel.Error => ("ERR", Ink.Error),
			_ => ("CRT", Ink.Error)
		};
		return $"{_tokens.Wrap(Ink.Muted, line.Time.ToLocalTime().ToString("HH:mm:ss"))}  " +
		       $"{_tokens.Wrap(token, name)}  {_tokens.Wrap(Ink.Text, line.Message)}";
	}

	private string Format(OutputLine line)
	{
		var ink = line.Kind switch
		{
			OutputKind.Error => Ink.Error,
			OutputKind.Ok => Ink.Success,
			OutputKind.Header => Ink.Muted,
			_ => Ink.Text
		};
		return "  " + _tokens.Wrap(ink, line.Text);
	}

	private void OnAccepted()
	{
		var line = Prompt?.Text?.Trim() ?? string.Empty;
		Prompt?.Text = string.Empty;
		if (line.Length == 0) return;

		var shown = Redactor.Redact(_runner.Catalog, line);
		_history.Add(shown);
		_log.AppendMarkupLine(_tokens.Wrap(Ink.Accent, $"{_glyphs.Prompt} {shown}"));
		_ = RunAsync(line);
	}

	private async Task RunAsync(string line)
	{
		var output = new LogOutput(this);
		try
		{
			var session = new CommandSession(true, AskAsync);
			await _runner.RunAsync(CommandCatalog.Split(line), output, true, session, _cancel.Token);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.LogError(exception, "A console command failed");
			output.Error("internal", "The command failed; see the log.");
		}
	}

	private Task<bool> AskAsync(string question, CancellationToken ct)
	{
		var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		ct.Register(() => source.TrySetCanceled());
		_app?.Post(() =>
		{
			var dialog = new Dialog().Title("Confirm");
			void Answer(bool yes)
			{
				dialog.Close();
				source.TrySetResult(yes);
			}

			dialog.Content(new VStack(new TextBlock(question),
				new HStack(new Button("Yes").Click(() => Answer(true)), new Button("No").Click(() => Answer(false)))
					.Spacing(2)));
			dialog.Show();
		});
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

	private sealed class LogOutput(ConsoleTab tab) : ICommandOutput
	{
		/// <inheritdoc />
		public void Write(OutputLine line)
		{
			var text = tab.Format(line);
			tab._app?.Post(() =>
			{
				foreach (var part in text.Split('\n')) tab._log.AppendMarkupLine(part);
			});
		}
	}
}
