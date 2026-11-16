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
	/// <param name="fields">The labels of the values to ask for, with their starting text.</param>
	/// <param name="submit">Receives the values in the order of <paramref name="fields" />.</param>
	public void Form(string title, IReadOnlyList<(string Label, string Start)> fields,
		Action<IReadOnlyList<string>> submit)
	{
		var dialog = new Dialog().Title(title);
		var boxes = fields.Select(field => new TextBox().Text(field.Start)).ToList();
		var width = fields.Max(field => field.Label.Length) + 1;
		var rows = new VStack();
		// Each box sits in its own frame, so that an empty one is still a visible field.
		for (var i = 0; i < fields.Count; i++)
			rows.Add(new HStack(new TextBlock(fields[i].Label.PadRight(width)).VerticalAlignment(Align.Center),
				new Group().Style(GroupStyle.Single).Content(boxes[i]).MinWidth(FieldWidth)));
		rows.Add(Buttons(new Button("OK").Click(() =>
		{
			dialog.Close();
			submit([.. boxes.Select(box => box.Text ?? string.Empty)]);
		}), new Button("Cancel").Click(dialog.Close)));
		dialog.Content(EscapeCloses(dialog, rows));
		Show(dialog);
	}
}
