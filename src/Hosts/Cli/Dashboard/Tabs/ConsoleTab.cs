using System.Text;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Text;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>
///     The console: the log of the server written straight on the screen, and, at the full level, the command box under
///     it whose output goes into the same log.
/// </summary>
internal sealed class ConsoleTab
{
	private const int PopupSize = 8;
	private const int WordWidth = 30;

	private readonly CancellationTokenSource _cancel = new();
	private readonly Glyphs _glyphs;
	private readonly LogBuffer _logBuffer;
	private readonly ILogger _logger;
	private readonly LogControl _log;
	private readonly CommandRunner _runner;
	private readonly CommandSuggestions _suggestions;
	private readonly Palette _tokens;
	private readonly State<bool> _popupOpen = new(false);
	private readonly State<int> _popupRows = new(0);
	private readonly State<string>[] _popupText = [.. Enumerable.Range(0, PopupSize).Select(_ => new State<string>(string.Empty))];
	private readonly List<string> _past = [];
	private int _pastIndex;
	private IReadOnlyList<(string Word, string Description)> _items = [];
	private string _lastText = string.Empty;
	private int _lastCaret;
	private string _closedAt = string.Empty;
	private int _replaceStart;
	private int _replaceLength;
	private int _selected;
	private int _top;
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
			.CompletionPresentation(PromptEditorCompletionPresentation.None)
			.EnableGhostCompletion(false)
			.Highlighter(Highlight)
			.Accepted(OnAccepted);
		AddPopupKeys(Prompt);
		var rows = new VStack().Spacing(0);
		for (var i = 0; i < PopupSize; i++)
		{
			var row = i;
			rows.Add(new Markup(() => _popupText[row].Value).IsVisible(() => row < _popupRows.Value)
				.PointerPressed(() => AcceptAt(_top + row)));
		}

		var popup = new Group().Style(options.Ascii ? GroupStyle.Ascii : GroupStyle.Rounded).Content(rows)
			.IsVisible(_popupOpen).Stretch();
		View = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Star() }, new RowDefinition { Height = GridLength.Auto },
				new RowDefinition { Height = GridLength.Auto })
			.Cell(_log, 0, 0)
			.Cell(popup, 1, 0)
			.Cell(Prompt, 2, 0);
	}

	/// <summary>Gets what the tab shows.</summary>
	public Visual View { get; }

	/// <summary>Gets the command box, or <see langword="null" /> when the console cannot change the server.</summary>
	public PromptEditor? Prompt { get; }

	/// <summary>Connects the tab to the running application.</summary>
	public void Attach(TerminalApp app)
	{
		_app = app;
		Activate();
	}

	/// <summary>Gives the focus to the command box; call it when the tab comes into view.</summary>
	public void Activate()
	{
		if (Prompt is not null) _app?.Focus(Prompt);
	}

	/// <summary>Stops the commands the tab still runs.</summary>
	public void Stop()
	{
		_cancel.Cancel();
	}

	/// <summary>Moves the lines logged since the last call into the log control.</summary>
	public void Pump()
	{
		if (Prompt is not null && (Prompt.Text ?? string.Empty) is var text && text != _lastText)
		{
			_lastText = text;
			Suggest(false);
		}
		else if (Prompt is not null && Prompt.CaretIndex != _lastCaret && _popupOpen.Value)
		{
			Suggest(false);
		}

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

	/// <summary>Runs a command as if the operator had typed it, showing it and its output in the log.</summary>
	/// <param name="line">The command line.</param>
	/// <param name="sink">Also receives each output line, on the thread that produced it, or <see langword="null" />.</param>
	/// <returns>A task that ends when the command has finished.</returns>
	public Task RunCommand(string line, Action<OutputLine>? sink = null)
	{
		var shown = Redactor.Redact(_runner.Catalog, line);
		_app?.Post(() => _log.AppendMarkupLine(_tokens.Wrap(Ink.Accent, $"{_glyphs.Prompt} {shown}")));
		return RunAsync(line, sink);
	}

	private void OnAccepted()
	{
		// Enter on a suggestion completes it; on a word that is already complete it runs the line.
		if (_popupOpen.Value && _items.Count > 0 && !EndsWith(_items[_selected].Word))
		{
			AcceptAt(_selected);
			return;
		}

		var line = Prompt?.Text?.Trim() ?? string.Empty;
		Prompt?.Text = string.Empty;
		if (line.Length == 0) return;

		var shown = Redactor.Redact(_runner.Catalog, line);
		_past.Remove(shown);
		_past.Add(shown);
		_pastIndex = _past.Count;
		_log.AppendMarkupLine(_tokens.Wrap(Ink.Accent, $"{_glyphs.Prompt} {shown}"));
		_ = RunAsync(line);
	}

	private async Task RunAsync(string line, Action<OutputLine>? sink = null)
	{
		var output = new LogOutput(this, sink);
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

	private Action? _decline;

	/// <summary>Whether a confirmation is waiting for an answer.</summary>
	public bool Asking => _decline is not null;

	/// <summary>Answers the waiting confirmation with "no".</summary>
	public void Decline()
	{
		_decline?.Invoke();
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
				_decline = null;
				dialog.Close();
				source.TrySetResult(yes);
			}

			_decline = () => Answer(false);

			dialog.Content(Dialogs.EscapeCloses(dialog, new VStack(new TextBlock(question + " (y/n)"),
				new HStack(new Button("Yes").Click(() => Answer(true)), new Button("No").Click(() => Answer(false)))
					.Spacing(2)).KeyDown((_, e) =>
			{
				if (e.Char is not ('y' or 'Y' or 'n' or 'N')) return;
				e.Handled = true;
				Answer(e.Char is 'y' or 'Y');
			}), () => Answer(false)));
			dialog.Show();
		});
		return source.Task;
	}

	// The popup above the command box: the words that fit the end of the typed line, with what each does.
	private void Suggest(bool force)
	{
		var text = Prompt?.Text ?? string.Empty;
		_lastCaret = Prompt?.CaretIndex ?? 0;
		var before = text[..Math.Clamp(_lastCaret, 0, text.Length)];
		if (before.Length == 0 || (!force && (text == _closedAt || _past.Contains(text))))
		{
			ClosePopup();
			return;
		}

		try
		{
			var (items, start, length) = _suggestions.SuggestDescribed(before);
			_items = items;
			_replaceStart = start;
			_replaceLength = length;
		}
		catch (InvalidOperationException exception)
		{
			// The server changed the ids while they were listed; the next key tries again.
			_logger.LogDebug(exception, "The console suggestions changed while they were read");
			_items = [];
		}

		_selected = 0;
		_top = 0;
		if (_items.Count == 0)
		{
			ClosePopup();
			return;
		}

		ShowPopup();
	}

	private void ClosePopup()
	{
		_items = [];
		_popupOpen.Value = false;
		_closedAt = Prompt?.Text ?? string.Empty;
	}

	private void ShowPopup()
	{
		if (_selected < _top) _top = _selected;
		else if (_selected >= _top + PopupSize) _top = _selected - PopupSize + 1;

		var shown = Math.Min(PopupSize, _items.Count - _top);
		var width = Math.Min(WordWidth, _items.Skip(_top).Take(shown).Max(item => item.Word.Length));
		for (var i = 0; i < PopupSize; i++)
		{
			if (i >= shown)
			{
				_popupText[i].Value = string.Empty;
				continue;
			}

			var (word, description) = _items[_top + i];
			var cell = Palette.Plain(word.Length > WordWidth ? word[..(WordWidth - 1)] + "…" : word).PadRight(width);
			var row = XenoAtom.Ansi.AnsiMarkup.Escape($" {cell}  ") + _tokens.Wrap(Ink.Muted, description);
			_popupText[i].Value = _top + i == _selected
				? $"[invert]{XenoAtom.Ansi.AnsiMarkup.Escape($" {cell}  ")}[/]{_tokens.Wrap(Ink.Muted, description)}"
				: row;
		}

		_popupRows.Value = shown;
		_popupOpen.Value = true;
	}

	private void Move(int delta)
	{
		_selected = Math.Clamp(_selected + delta, 0, _items.Count - 1);
		ShowPopup();
	}

	private void AcceptAt(int index)
	{
		if (Prompt is null || index < 0 || index >= _items.Count) return;

		var text = Prompt.Text ?? string.Empty;
		var head = text[.._replaceStart];
		var tail = _replaceStart + _replaceLength <= text.Length ? text[(_replaceStart + _replaceLength)..] : string.Empty;
		// The popup stays closed until the operator types again, so that Enter then runs the line.
		var accepted = $"{head}{_items[index].Word} {tail}";
		_closedAt = accepted;
		_lastText = accepted;
		Prompt.Text = accepted;
		Prompt.CaretIndex = head.Length + _items[index].Word.Length + 1;
		_lastCaret = Prompt.CaretIndex;
		ClosePopup();
		_app?.Focus(Prompt);
	}

	private void AddPopupKeys(PromptEditor prompt)
	{
		bool Open() => _popupOpen.Value && _items.Count > 0;
		prompt.AddCommand(new Command
		{
			Id = "console.suggest.up", LabelMarkup = "Previous suggestion", Gesture = new KeyGesture(TerminalKey.Up),
			Execute = _ => { if (Open()) Move(-1); else Recall(-1); }
		});
		prompt.AddCommand(new Command
		{
			Id = "console.suggest.down", LabelMarkup = "Next suggestion", Gesture = new KeyGesture(TerminalKey.Down),
			Execute = _ => { if (Open()) Move(1); else Recall(1); }
		});
		prompt.AddCommand(new Command
		{
			Id = "console.suggest.tab", LabelMarkup = "Complete", Gesture = new KeyGesture(TerminalKey.Tab),
			Execute = _ =>
			{
				if (Open())
				{
					AcceptAt(_selected);
					return;
				}

				Suggest(true);
				if (_items.Count == 1) AcceptAt(0);
			}
		});
		prompt.AddCommand(new Command
		{
			Id = "console.suggest.back", LabelMarkup = "Close", Gesture = new KeyGesture(TerminalKey.Left, TerminalModifiers.Alt),
			CanExecute = _ => Open(), Execute = _ => ClosePopup()
		});
		// The editor takes Esc for itself and reports it as canceled: close the popup, else clear the line.
		prompt.Canceled(() =>
		{
			if (Open()) ClosePopup();
			else ClearPrompt();
		});
	}

	// Up and Down with the popup closed walk through the commands run before.
	private void Recall(int delta)
	{
		if (Prompt is null) return;

		_pastIndex = Math.Clamp(_pastIndex + delta, 0, _past.Count);
		var text = _pastIndex < _past.Count ? _past[_pastIndex] : string.Empty;
		_closedAt = text;
		_lastText = text;
		Prompt.Text = text;
		Prompt.CaretIndex = text.Length;
		ClosePopup();
	}

	private bool EndsWith(string word)
	{
		var text = Prompt?.Text ?? string.Empty;
		return text.EndsWith(word, StringComparison.OrdinalIgnoreCase) && _replaceStart + word.Length == text.Length;
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

	private sealed class LogOutput(ConsoleTab tab, Action<OutputLine>? sink = null) : ICommandOutput
	{
		/// <inheritdoc />
		public void Write(OutputLine line)
		{
			sink?.Invoke(line);
			var parts = line.Text.Split('\n').Select(part => tab.Format(line with { Text = part.TrimEnd('\r') })).ToList();
			tab._app?.Post(() =>
			{
				foreach (var part in parts) tab._log.AppendMarkupLine(part);
			});
		}
	}
}
