using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed record Glyphs(bool Ascii)
{
	public string Prompt => Ascii ? ">" : "❯";

	public string Dot => Ascii ? "|" : "·";

	public string Yes => Ascii ? "y" : "✓";

	public string No => Ascii ? "n" : "×";

	public string ToastInfo => Ascii ? "+" : "✓";

	public static string ToastWarning => "!";

	public string ToastError => Ascii ? "x" : "✗";

	public string Open => Ascii ? "v" : "▾";

	public string Folded => Ascii ? ">" : "▸";

	public char Rule => Ascii ? '-' : '─';

	public string Crumb => Ascii ? ">" : "›";

	public string Refresh => Ascii ? "@" : "↻";

	public string Back => Ascii ? "<" : "←";

	public string Down => Ascii ? "v" : "↓";

	// One cell wide, for a text cut to fit its column.
	public string Cut => Ascii ? "~" : "…";

	public GroupStyle Frame => Ascii ? GroupStyle.Ascii : GroupStyle.Rounded;

	public GroupStyle Box => Ascii ? GroupStyle.Ascii : GroupStyle.Single;

	// Text for the screen: with --ascii every character outside ASCII is spelled with ASCII ones.
	public string Text(string text)
	{
		return Ascii ? AsciiText.Of(text) : text;
	}
}