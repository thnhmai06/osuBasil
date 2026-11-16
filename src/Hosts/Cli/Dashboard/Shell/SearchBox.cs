using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>
///     The search row of a pane: while the box is empty and not focused it is only as wide as its placeholder; once it has
///     the focus or holds text it reaches the right edge of its frame.
/// </summary>
internal sealed class SearchBox
{
	private const int Margin = 3;
	private readonly State<bool> _wide = new(false);

	/// <summary>Creates the row.</summary>
	/// <param name="query">The text of the box.</param>
	/// <param name="placeholder">What the empty box says.</param>
	/// <param name="glyphs">The characters to draw with.</param>
	public SearchBox(State<string> query, string placeholder, Glyphs glyphs)
	{
		Query = query;
		var narrow = placeholder.Length + Margin;
		Box = new TextBox().Text(query).Placeholder(placeholder).MaxWidth(() => _wide.Value ? int.MaxValue : narrow)
			.HorizontalAlignment(() => _wide.Value ? Align.Stretch : Align.Start);
		// A stack gives the box only the width it asks for; the star column of a grid gives it the rest of the row.
		Row = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Star() })
			.Rows(new RowDefinition { Height = GridLength.Auto })
			.Cell(new TextBlock($"{(glyphs.Ascii ? "?" : "⌕")} "), 0, 0)
			.Cell(Box, 0, 1);
	}

	/// <summary>Gets the text of the box.</summary>
	public State<string> Query { get; }

	/// <summary>Gets the box itself, which takes the focus.</summary>
	public TextBox Box { get; }

	/// <summary>Gets the row to put in the pane.</summary>
	public Visual Row { get; }

	/// <summary>Widens or shortens the box after the focus moved; call it on every pass of the loop.</summary>
	public void Tick()
	{
		_wide.Value = Box.HasFocus || Query.Value.Length > 0;
	}
}
