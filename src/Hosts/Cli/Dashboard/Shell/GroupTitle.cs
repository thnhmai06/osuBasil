using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

internal static class GroupTitle
{
	public static void Draw(CellBuffer buffer, Rectangle rect, int y, Glyphs glyphs, bool noColor, Theme theme,
		string name, string count, bool open, bool selected)
	{
		var rule = glyphs.Rule;
		var arrow = open ? glyphs.Open : glyphs.Folded;
		var title = Palette.Plain(name);
		if (title.Length > 0) title = char.ToUpperInvariant(title[0]) + title[1..];
		var muted = noColor
			? Style.None.WithTextStyle(TextStyle.Dim)
			: Style.None.WithForeground(theme.Muted ?? Colors.TerminalBrightBlack);
		var bold = (noColor ? Style.None : Style.None.WithForeground(theme.Foreground ?? Colors.TerminalWhite))
			.WithTextStyle(TextStyle.Bold);
		if (selected)
		{
			muted = Select(muted, theme, noColor);
			bold = Select(bold, theme, noColor);
			buffer.WriteText(rect.X, y, new string(' ', rect.Width), muted);
		}

		var x = rect.X;

		void Put(string text, Style style)
		{
			var room = rect.X + rect.Width - x;
			if (room <= 0) return;

			var shown = text.Length > room ? text[..room] : text;
			buffer.WriteText(x, y, shown, style);
			x += shown.Length;
		}

		Put($"{arrow} {rule}{rule} ", muted);
		Put(title, bold);
		Put($" ({count}) ", muted);
		Put(new string(rule, Math.Max(rect.X + rect.Width - x, 0)), muted);
	}

	private static Style Select(Style style, Theme theme, bool noColor)
	{
		return noColor
			? style.WithTextStyle(TextStyle.Invert)
			: style.WithBackground(theme.Selection ?? Colors.TerminalBlue);
	}
}
