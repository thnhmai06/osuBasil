namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed record Glyphs(bool Ascii)
{
	public string Prompt => Ascii ? ">" : "❯";

	public string Dot => Ascii ? "|" : "·";

	public string Yes => Ascii ? "y" : "✓";

	public string No => Ascii ? "n" : "×";

	public string ToastInfo => Ascii ? "+" : "✓";

	public string ToastWarning => "!";

	public string ToastError => Ascii ? "x" : "✗";

	public string Open => Ascii ? "v" : "▾";

	public string Folded => Ascii ? ">" : "▸";

	public char Rule => Ascii ? '-' : '─';

	public string Crumb => Ascii ? ">" : "›";

	public string Refresh => Ascii ? "@" : "↻";

	public string Back => Ascii ? "<" : "←";

	public string Down => Ascii ? "v" : "↓";
}