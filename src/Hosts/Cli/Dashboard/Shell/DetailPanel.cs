using Basil.Host.Cli.Dashboard.Shell.Toasts;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>
///     The frame under a list that shows one item and the actions on it. It is a third of the terminal high whatever
///     it shows, the fields scroll on the left and the buttons stand in a column of fixed width on the right. An
///     action that leads to another tab replaces what the frame shows and adds a step to the breadcrumb on its border.
/// </summary>
internal sealed class DetailPanel
{
	private const int CompactWidth = 100;
	private const int ButtonColumn = 22;
	private const int CompactButtonColumn = 14;
	private readonly Glyphs _glyphs;
	private readonly Dialogs _dialogs;
	private readonly Group _box;
	private readonly State<bool> _open = new(false);
	private readonly State<int> _height = new(8);
	private readonly State<string> _crumb = new(string.Empty);
	private readonly Palette _palette;
	private readonly Action<Action> _post;
	private readonly string _root;
	private readonly List<DetailView> _steps = [];
	private readonly ToastTray _toasts;
	private TerminalApp? _app;
	private bool _compact;

	/// <summary>Creates the frame.</summary>
	/// <param name="root">The name of the tab, which starts the breadcrumb.</param>
	/// <param name="glyphs">The characters to draw with.</param>
	/// <param name="palette">Colours the field values.</param>
	/// <param name="dialogs">Shows the dialog that holds the actions in a narrow terminal.</param>
	/// <param name="toasts">Tells the operator when an action failed.</param>
	/// <param name="post">Runs an action on the thread of the interface.</param>
	/// <param name="changes">Tells the frame to read its steps again when an action changed something.</param>
	public DetailPanel(string root, Glyphs glyphs, Palette palette, Dialogs dialogs, ToastTray toasts, Action<Action> post,
		Changes changes)
	{
		changes.Occurred += Reload;
		_root = root;
		_glyphs = glyphs;
		_palette = palette;
		_dialogs = dialogs;
		_toasts = toasts;
		_post = post;
		_box = new Group(new Markup(() => _crumb.Value)).Style(glyphs.Ascii ? GroupStyle.Ascii : GroupStyle.Single)
			.IsVisible(_open).MinHeight(() => _height.Value).MaxHeight(() => _height.Value).HorizontalAlignment(Align.Stretch);
	}

	/// <summary>Gets the frame to put under a list.</summary>
	public Visual View => _box;

	/// <summary>Gets whether the frame is open.</summary>
	public bool IsOpen => _open.Value;

	/// <summary>Occurs when the frame closes.</summary>
	public event Action? Closed;

	/// <summary>Connects the frame to the running application.</summary>
	public void Attach(TerminalApp app)
	{
		_app = app;
	}

	/// <summary>Shows an item and opens the frame, dropping the steps taken from the item before.</summary>
	public void Show(DetailView view)
	{
		_steps.Clear();
		_steps.Add(view);
		Fill();
		_open.Value = true;
	}

	/// <summary>Opens the frame on a view that is read first, dropping the steps taken before; a loading state stands meanwhile.</summary>
	/// <param name="title">The name of the item, shown while it loads.</param>
	/// <param name="read">Reads and builds the view off the interface thread.</param>
	public void ShowAsync(string title, Func<Task<DetailView>> read)
	{
		_steps.Clear();
		_open.Value = true;
		Load(title, read);
	}

	/// <summary>Shows fresh data of the item the frame stands on, without leaving the step the operator is in.</summary>
	public void Update(DetailView view)
	{
		if (_steps.Count > 1) return;

		Show(view);
	}

	/// <summary>Goes back one step, or closes the frame when it is on its first.</summary>
	public void Back()
	{
		if (_steps.Count > 1)
		{
			_steps.RemoveAt(_steps.Count - 1);
			Fill();
			return;
		}

		Close();
	}

	/// <summary>Closes the frame.</summary>
	public void Close()
	{
		if (!_open.Value) return;

		_open.Value = false;
		_steps.Clear();
		Closed?.Invoke();
	}

	/// <summary>Keeps the frame a third of the terminal high, and folds its actions into one button below 100 columns.</summary>
	/// <param name="width">The columns of the terminal.</param>
	/// <param name="rows">The rows of the terminal.</param>
	public void Fit(int width, int rows)
	{
		if (width <= 0 || rows <= 0) return;

		_height.Value = Math.Max(rows / 3, 4);
		var compact = width < CompactWidth;
		if (compact == _compact) return;

		_compact = compact;
		if (_open.Value) Fill();
	}

	// Reads every step that knows how again, so that what an action changed shows up in the frame, nested steps included.
	// A step that cannot be read now stays as it was.
	private void Reload()
	{
		foreach (var step in _steps.Where(step => step.Reload is not null).ToList())
			_ = Task.Run(async () =>
			{
				try
				{
					var view = await step.Reload!();
					_post(() => Replace(step, view));
				}
				catch (Exception)
				{
					// The stale step stays.
				}
			});
	}

	// Pushes a loading step of the frame's size, reads off the interface thread, and posts the result back.
	// A result for a step the operator already left is dropped.
	private void Load(string title, Func<Task<DetailView>> read)
	{
		var pending = new DetailView(title, [("Status", "Loading…", Ink.Muted)], []);
		_steps.Add(pending);
		Fill();
		_ = Task.Run(async () =>
		{
			try
			{
				var view = await read();
				_post(() => Replace(pending, view));
			}
			catch (Exception exception)
			{
				_post(() => Drop(pending, title, exception));
			}
		});
	}

	private void Replace(DetailView pending, DetailView view)
	{
		var at = _steps.FindIndex(step => ReferenceEquals(step, pending));
		if (at < 0) return;

		_steps[at] = view;
		if (at == _steps.Count - 1) Fill();
	}

	private void Drop(DetailView pending, string title, Exception exception)
	{
		var at = _steps.FindIndex(step => ReferenceEquals(step, pending));
		if (at < 0) return;

		_toasts.Show($"{title}: {exception.Message}", ToastKind.Error);
		_steps.RemoveAt(at);
		if (_steps.Count == 0) Close();
		else if (at == _steps.Count) Fill();
	}

	private void Push(DetailView view)
	{
		_post(() =>
		{
			_steps.Add(view);
			Fill();
		});
	}

	private void Fill()
	{
		var view = _steps[^1];
		_crumb.Value = string.Join($" {_palette.Wrap(Ink.Muted, _glyphs.Crumb)} ",
			_steps.Select(step => Palette.Escape(step.Title)).Prepend(Palette.Escape(_root)));
		var width = view.Fields.Count == 0 ? 0 : view.Fields.Max(field => field.Label.Length);
		var fields = new VStack().Spacing(0);
		foreach (var (label, value, kind) in view.Fields)
		{
			// A string, not an interpolated string: the compiler would send the latter to the overload that escapes the tags.
			var line = _palette.Wrap(Ink.Muted, label.PadRight(width)) + " " + _palette.Wrap(kind, value);
			fields.Add(new Markup(line));
		}
		var buttons = new List<Button>();
		if (_steps.Count > 1) buttons.Add(new Button($"[{_glyphs.Back} Back]").Click(Back));
		var ordered = view.Actions.Append(new DetailAction("Close", Close, DetailActionKind.Close)).OrderBy(action => action.Kind).ToList();
		if (_compact && ordered.Count > 0)
			buttons.Add(new Button("[Actions]").Click(() => ShowActions(ordered)));
		else
			buttons.AddRange(ordered.Select(Button));
		var column = new VStack().Spacing(0);
		foreach (var button in buttons) column.Add(button);
		var body = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star() },
				new ColumnDefinition { Width = GridLength.Fixed(_compact ? CompactButtonColumn : ButtonColumn) })
			.Rows(new RowDefinition { Height = GridLength.Star() })
			.Cell(view.Body is { } table ? table(Push) : new ScrollViewer(fields).Stretch(), 0, 0)
			.Cell(new ScrollViewer(column).Stretch(), 0, 1);
		_box.Content(body.KeyDown((_, e) => OnKey(e, buttons)));
	}

	private Button Button(DetailAction action)
	{
		var button = new Button(action.Label).Click(() => Run(action)).HorizontalAlignment(Align.Stretch);
		return action.Kind == DetailActionKind.Destructive ? button.Tone(ControlTone.Error) : button;
	}

	// An action touches the interface (a dialog, the selected tab, state), so it runs on the thread of the interface;
	// when it fails, the operator is told.
	private void Run(DetailAction action)
	{
		_post(() =>
		{
			try
			{
				if (action.Children is { } children)
				{
					ShowActions(children, action.Label.TrimEnd('…', '›', '>', ' '));
				}
				else if (action.NextAsync is { } read)
				{
					Load(action.Label.TrimEnd('…'), read);
				}
				else if (action.Next is { } next)
				{
					_steps.Add(next());
					Fill();
				}
				else
				{
					action.Run();
				}
			}
			catch (Exception exception)
			{
				_toasts.Show($"{action.Label.TrimEnd('…')}: {exception.Message}", ToastKind.Error);
			}
		});
	}

	private void OnKey(KeyEventArgs e, List<Button> buttons)
	{
		if (e.Char == 'x')
		{
			Close();
		}
		else if (e.Key is TerminalKey.Up or TerminalKey.Down && buttons.Count > 0)
		{
			var at = buttons.FindIndex(button => ReferenceEquals(button, _app?.FocusedElement));
			var step = e.Key == TerminalKey.Up ? -1 : 1;
			_app?.Focus(buttons[Math.Clamp(at + step, 0, buttons.Count - 1)]);
		}
		else
		{
			return;
		}

		e.Handled = true;
	}

	private void ShowActions(IReadOnlyList<DetailAction> actions, string title = "Actions")
	{
		var dialog = new Dialog().Title(title);
		var rows = new VStack().Spacing(0);
		foreach (var action in actions)
		{
			var button = new Button(action.Label).Click(() =>
			{
				dialog.Close();
				Run(action);
			}).HorizontalAlignment(Align.Stretch);
			rows.Add(action.Kind == DetailActionKind.Destructive ? button.Tone(ControlTone.Error) : button);
		}

		var list = new ScrollViewer(rows).MinWidth(24).MaxHeight(Math.Max(_height.Value, 6));
		dialog.Content(Dialogs.EscapeCloses(dialog, new VStack(list, Dialogs.Buttons(new Button("Cancel").Click(dialog.Close)))));
		_dialogs.Show(dialog);
	}
}
