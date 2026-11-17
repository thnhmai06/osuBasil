using Basil.Domain.Users;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>The small modal forms of the dashboard, and the dim behind them while one is open.</summary>
internal sealed class Dialogs
{
	private const int FieldWidth = 30;
	private const int TextWidth = 44;
	private readonly List<Dialog> _shown = [];
	private readonly State<int> _open = new(0);

	/// <summary>Puts a dim layer over a page while a dialog is open; with none open, the page takes the pointer itself.</summary>
	/// <param name="page">What the dialogs open over.</param>
	public Visual Dim(Visual page)
	{
		return new ZStack(page, new Backdrop().IsVisible(() => _open.Value > 0));
	}

	/// <summary>Shows a dialog and dims the page behind it until it closes; call it on the thread of the interface.</summary>
	/// <param name="dialog">The dialog to show.</param>
	public void Show(Dialog dialog)
	{
		// One column of space between the border and the content, as every frame of the dashboard has.
		dialog.Padding(new Thickness(1, 0, 1, 0)).Show();
		_shown.Add(dialog);
		_open.Value = _shown.Count;
	}

	/// <summary>Lifts the dim of the dialogs that closed; the dashboard calls it on every pass of its loop.</summary>
	public void Settle()
	{
		if (_shown.RemoveAll(dialog => dialog.App is null) > 0) _open.Value = _shown.Count;
	}

	/// <summary>Makes Esc inside the content close the dialog.</summary>
	/// <param name="dialog">The dialog to close.</param>
	/// <param name="content">The content of the dialog.</param>
	/// <param name="onEscape">What to do besides closing, such as answering "no".</param>
	/// <returns>The same content.</returns>
	public static T EscapeCloses<T>(Dialog dialog, T content, Action? onEscape = null) where T : Visual
	{
		foreach (var gesture in BackGestures)
			content.AddCommand(new Command
			{
				Id = $"dialog.back.{gesture}", LabelMarkup = "Back", Gesture = gesture, Execute = _ =>
				{
					dialog.Close();
					onEscape?.Invoke();
				}
			});

		return content;
	}

	/// <summary>Puts buttons in one row, centred in the dialog.</summary>
	/// <param name="buttons">The buttons, left to right.</param>
	public static Visual Buttons(params Button[] buttons)
	{
		return new HStack(buttons).Spacing(2).HorizontalAlignment(Align.Center);
	}

	/// <summary>Esc, and Alt+Left for terminals that delay or swallow Esc: the gestures that mean "back".</summary>
	public static readonly KeyGesture[] BackGestures =
		[new(TerminalKey.Escape), new(TerminalKey.Left, TerminalModifiers.Alt)];

	/// <summary>Shows lines of text with a Close button; Esc, Enter and Alt+Left close it too.</summary>
	/// <param name="title">The title of the dialog.</param>
	/// <param name="lines">The text.</param>
	public void Info(string title, IEnumerable<string> lines)
	{
		var dialog = new Dialog().Title(title);
		var rows = new VStack();
		foreach (var line in lines) rows.Add(new TextBlock(line));
		rows.Add(Buttons(new Button("Close").Click(dialog.Close)));
		dialog.Content(EscapeCloses(dialog, rows).KeyDown((_, e) =>
		{
			if (e.Key != TerminalKey.Enter) return;
			e.Handled = true;
			dialog.Close();
		}));
		Show(dialog);
	}

	/// <summary>Asks for a few text values and calls back with them when the operator confirms.</summary>
	/// <param name="title">The title of the dialog.</param>
	/// <param name="fields">The values to ask for.</param>
	/// <param name="submit">Receives the values in the order of <paramref name="fields" />.</param>
	public void Form(string title, IReadOnlyList<FormField> fields, Action<IReadOnlyList<string>> submit)
	{
		var dialog = new Dialog().Title(title);
		var boxes = fields.Select(field => new TextBox().Text(field.Start).IsPassword(field.Secret)).ToList();
		var width = fields.Max(field => field.Label.Length) + 1;
		var rows = new VStack();
		var done = false;

		void Ok()
		{
			if (done) return;

			done = true;
			dialog.Close();
			submit([.. boxes.Select(box => box.Text ?? string.Empty)]);
		}

		// Enter in a box submits, like a web form.
		foreach (var box in boxes)
			box.KeyDown((_, e) =>
			{
				if (e.Key != TerminalKey.Enter) return;
				e.Handled = true;
				Ok();
			});

		// Each box sits in its own frame, so that an empty one is still a visible field.
		for (var i = 0; i < fields.Count; i++)
			rows.Add(new HStack(new TextBlock(fields[i].Label.PadRight(width)).VerticalAlignment(Align.Center),
				new Group().Style(GroupStyle.Single).Content(boxes[i]).MinWidth(FieldWidth)));
		rows.Add(Buttons(new Button("OK").Click(Ok), new Button("Cancel").Click(dialog.Close)));
		dialog.Content(EscapeCloses(dialog, rows));
		Show(dialog);
	}

	/// <summary>Asks the operator to confirm something that cannot be undone, saying exactly what will happen.</summary>
	/// <param name="title">The title of the dialog.</param>
	/// <param name="consequence">What the action does, in plain sentences.</param>
	/// <param name="yes">The text of the button that goes ahead, such as <c>Delete</c>.</param>
	/// <param name="confirmed">Runs when the operator goes ahead.</param>
	public void Confirm(string title, string consequence, string yes, Action confirmed)
	{
		var dialog = new Dialog().Title(title);
		var rows = new VStack();
		foreach (var line in WrapText(consequence, TextWidth)) rows.Add(new TextBlock(line));
		var cancel = new Button("Cancel").Click(dialog.Close).AutoFocus(true);
		rows.Add(Buttons(new Button(yes).Tone(ControlTone.Error).Click(() =>
		{
			dialog.Close();
			confirmed();
		}), cancel));
		dialog.Content(EscapeCloses(dialog, rows));
		Show(dialog);
	}

	/// <summary>Asks the operator to pick one of a few options.</summary>
	/// <param name="title">The title of the dialog.</param>
	/// <param name="options">The options; the one equal to <paramref name="current" /> is marked.</param>
	/// <param name="current">The option in effect now, or <see langword="null" />.</param>
	/// <param name="chosen">Receives the picked option.</param>
	public void Choose(string title, IReadOnlyList<string> options, string? current, Action<string> chosen)
	{
		var dialog = new Dialog().Title(title);
		var rows = new VStack().Spacing(0);
		foreach (var option in options)
			rows.Add(new Button(option == current ? $"{option} (now)" : option).HorizontalAlignment(Align.Stretch)
				.Click(() =>
				{
					dialog.Close();
					chosen(option);
				}));
		dialog.Content(EscapeCloses(dialog, new VStack(new ScrollViewer(rows).MaxHeight(12),
			Buttons(new Button("Cancel").Click(dialog.Close)))));
		Show(dialog);
	}

	/// <summary>Asks the operator to switch some options on or off.</summary>
	/// <param name="title">The title of the dialog.</param>
	/// <param name="options">The options and whether each is on now.</param>
	/// <param name="submit">Receives whether each option is on, in the order of <paramref name="options" />.</param>
	public void Checklist(string title, IReadOnlyList<(string Label, bool On)> options, Action<IReadOnlyList<bool>> submit)
	{
		var dialog = new Dialog().Title(title);
		var states = options.Select(option => new State<bool>(option.On)).ToList();
		var rows = new VStack().Spacing(0);
		for (var i = 0; i < options.Count; i++) rows.Add(new CheckBox(options[i].Label).IsChecked(states[i]));
		dialog.Content(EscapeCloses(dialog, new VStack(new ScrollViewer(rows).MaxHeight(12), Buttons(new Button("OK").Click(() =>
		{
			dialog.Close();
			submit([.. states.Select(state => state.Value)]);
		}), new Button("Cancel").Click(dialog.Close)))));
		Show(dialog);
	}

	/// <summary>Asks the operator which privileges to grant, one box for each.</summary>
	/// <param name="title">The title of the dialog.</param>
	/// <param name="current">The privileges held now.</param>
	/// <param name="chosen">Receives the privileges whose boxes are ticked.</param>
	public void Privileges(string title, UserPrivileges current, Action<UserPrivileges> chosen)
	{
		var flags = Enum.GetValues<UserPrivileges>().Where(flag => flag != UserPrivileges.None).ToList();
		Checklist(title, [.. flags.Select(flag => (flag.ToString(), current.HasFlag(flag)))],
			ticked => chosen(flags.Where((_, i) => ticked[i]).Aggregate(UserPrivileges.None, (all, flag) => all | flag)));
	}

	/// <summary>Breaks text into lines no longer than a width, at spaces.</summary>
	/// <param name="text">The text.</param>
	/// <param name="width">The longest line.</param>
	public static IReadOnlyList<string> WrapText(string text, int width)
	{
		var lines = new List<string>();
		var line = new System.Text.StringBuilder();
		foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (line.Length > 0 && line.Length + 1 + word.Length > width)
			{
				lines.Add(line.ToString());
				line.Clear();
			}

			if (line.Length > 0) line.Append(' ');
			line.Append(word);
		}

		if (line.Length > 0) lines.Add(line.ToString());
		return lines;
	}
}
