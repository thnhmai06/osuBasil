using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed class SearchBox
{
	private const int Margin = 3;
	private readonly State<bool> _wide = new(false);

	public SearchBox(State<string> query, string placeholder, Glyphs glyphs)
	{
		Query = query;
		var narrow = placeholder.Length + Margin;
		Box = new TextBox().Text(query).Placeholder(placeholder).MaxWidth(() => _wide.Value ? int.MaxValue : narrow)
			.HorizontalAlignment(() => _wide.Value ? Align.Stretch : Align.Start);
		// A stack gives the box only the width it asks for; the star column of a grid gives it the rest of the row.
		Row = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Auto })
			.Cell(new TextBlock($"{(glyphs.Ascii ? "?" : "⌕")} "), 0, 0)
			.Cell(Box, 0, 1);
	}

	public State<string> Query { get; }

	public TextBox Box { get; }

	public Visual Row { get; }

	public void Tick()
	{
		_wide.Value = Box.HasFocus || Query.Value.Length > 0;
	}
}