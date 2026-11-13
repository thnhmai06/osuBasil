using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>The small modal forms of the dashboard.</summary>
internal static class Dialogs
{
	/// <summary>Makes Esc inside the content close the dialog.</summary>
	/// <param name="dialog">The dialog to close.</param>
	/// <param name="content">The content of the dialog.</param>
	/// <param name="onEscape">What to do besides closing, such as answering "no".</param>
	/// <returns>The same content.</returns>
	public static T EscapeCloses<T>(Dialog dialog, T content, Action? onEscape = null) where T : Visual
	{
		return content.KeyDown((_, e) =>
		{
			if (e.Key != TerminalKey.Escape) return;
			e.Handled = true;
			dialog.Close();
			onEscape?.Invoke();
		});
	}

	/// <summary>Asks for a few text values and calls back with them when the operator confirms.</summary>
	/// <param name="title">The title of the dialog.</param>
	/// <param name="fields">The labels of the values to ask for, with their starting text.</param>
	/// <param name="submit">Receives the values in the order of <paramref name="fields" />.</param>
	public static void Form(string title, IReadOnlyList<(string Label, string Start)> fields,
		Action<IReadOnlyList<string>> submit)
	{
		var dialog = new Dialog().Title(title);
		var boxes = fields.Select(field => new TextBox().Text(field.Start)).ToList();
		var rows = new VStack();
		for (var i = 0; i < fields.Count; i++)
			rows.Add(new HStack(new TextBlock($"{fields[i].Label,-10}"), boxes[i]));
		rows.Add(new HStack(new Button("OK").Click(() =>
		{
			dialog.Close();
			submit([.. boxes.Select(box => box.Text ?? string.Empty)]);
		}), new Button("Cancel").Click(dialog.Close)).Spacing(2));
		dialog.Content(EscapeCloses(dialog, rows));
		dialog.Show();
	}
}
