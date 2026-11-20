using System.Text;
using Basil.Domain.Users;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed class Dialogs
{
	private const int FieldWidth = 30;
	private const int TextWidth = 44;

	public static readonly KeyGesture[] BackGestures =
		[new(TerminalKey.Escape), new(TerminalKey.Left, TerminalModifiers.Alt)];

	private readonly State<int> _open = new(0);
	private readonly List<Dialog> _shown = [];

	public Visual Dim(Visual page)
	{
		return new ZStack(page, new Backdrop().IsVisible(() => _open.Value > 0));
	}

	public void Show(Dialog dialog)
	{
		// One column of space between the border and the content, as every frame of the dashboard has.
		dialog.Padding(new Thickness(1, 0, 1, 0)).Show();
		_shown.Add(dialog);
		_open.Value = _shown.Count;
	}

	public void Settle()
	{
		if (_shown.RemoveAll(dialog => dialog.App is null) > 0) _open.Value = _shown.Count;
	}

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

	public static Visual Buttons(params Button[] buttons)
	{
		return new HStack(buttons).Spacing(2).HorizontalAlignment(Align.Center);
	}

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

	public void Checklist(string title, IReadOnlyList<(string Label, bool On)> options,
		Action<IReadOnlyList<bool>> submit)
	{
		var dialog = new Dialog().Title(title);
		var states = options.Select(option => new State<bool>(option.On)).ToList();
		var rows = new VStack().Spacing(0);
		for (var i = 0; i < options.Count; i++) rows.Add(new CheckBox(options[i].Label).IsChecked(states[i]));
		dialog.Content(EscapeCloses(dialog, new VStack(new ScrollViewer(rows).MaxHeight(12), Buttons(
			new Button("OK").Click(() =>
			{
				dialog.Close();
				submit([.. states.Select(state => state.Value)]);
			}), new Button("Cancel").Click(dialog.Close)))));
		Show(dialog);
	}

	public void Privileges(string title, UserPrivileges current, Action<UserPrivileges> chosen)
	{
		var flags = Enum.GetValues<UserPrivileges>().Where(flag => flag != UserPrivileges.None).ToList();
		Checklist(title, [.. flags.Select(flag => (flag.ToString(), current.HasFlag(flag)))],
			ticked => chosen(flags.Where((_, i) => ticked[i])
				.Aggregate(UserPrivileges.None, (all, flag) => all | flag)));
	}

	public static IReadOnlyList<string> WrapText(string text, int width)
	{
		var lines = new List<string>();
		var line = new StringBuilder();
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