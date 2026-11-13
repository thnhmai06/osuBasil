using System.Text;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Scrolling;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>One column of a table.</summary>
/// <param name="Header">The title in the header row.</param>
/// <param name="Width">The width in cells; 0 takes what the other columns leave.</param>
/// <param name="Text">Reads the cell of an item.</param>
/// <param name="Kind">Tells what kind of text the cell holds, which decides its colour.</param>
internal sealed record Column<T>(string Header, int Width, Func<T, string> Text, Func<T, Ink>? Kind = null);

/// <summary>A titled group of items in a table.</summary>
/// <param name="Title">The name of the group, such as <c>Online</c>.</param>
/// <param name="Count">The size shown next to the title, such as <c>70</c> or <c>150+</c>.</param>
/// <param name="Items">The items.</param>
internal sealed record Section<T>(string Title, string Count, IReadOnlyList<T> Items);

/// <summary>
///     A table of items in titled groups: a header row that stays on top, the title of the group in view that stays
///     under it, a selected row, and a scroll bar when it is inside a <see cref="XenoAtom.Terminal.UI.Controls.ScrollViewer" />.
/// </summary>
internal sealed partial class GroupedList<T> : Visual, IScrollable
{
	/// <summary>Counts the changes of the content, which the framework tracks to redraw the table.</summary>
	[Bindable] public partial int Revision { get; set; }

	private readonly IReadOnlyList<Column<T>> _columns;
	private readonly Glyphs _glyphs;
	private readonly Func<T, string> _key;
	private readonly bool _noColor;
	private readonly ScrollModel _scroll;
	private List<Row> _rows = [];
	private int _selected = -1;
	private int _bodyHeight;

	/// <summary>Creates the table.</summary>
	/// <param name="columns">The columns.</param>
	/// <param name="key">Reads the id of an item, which keeps the selection when the items are replaced.</param>
	/// <param name="glyphs">The characters to draw with.</param>
	/// <param name="noColor">Whether the table is monochrome.</param>
	public GroupedList(IReadOnlyList<Column<T>> columns, Func<T, string> key, Glyphs glyphs, bool noColor)
	{
		_columns = columns;
		_key = key;
		_glyphs = glyphs;
		_noColor = noColor;
		_scroll = new ScrollModel(this);
		Focusable = true;
		HorizontalAlignment = Align.Stretch;
		VerticalAlignment = Align.Stretch;
		IsTabStop = true;
	}

	/// <inheritdoc />
	public ScrollModel Scroll => _scroll;

	/// <summary>Gets the selected item, or the default when nothing is selected.</summary>
	public T? Selected => _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Item : default;

	/// <summary>Gets whether an item is selected.</summary>
	public bool HasSelection => _selected >= 0 && _selected < _rows.Count;

	/// <summary>Gets how many items there are in all groups.</summary>
	public int ItemCount => _rows.Count(row => !row.IsHeader);

	/// <summary>Gets the position, counted among the items, of the first and the last item in view, from 1; 0 when none.</summary>
	public (int First, int Last) VisibleItems()
	{
		var first = 0;
		var last = 0;
		var number = 0;
		var from = _scroll.OffsetY;
		var to = from + Math.Max(_bodyHeight, 1);
		for (var i = 0; i < _rows.Count; i++)
		{
			if (_rows[i].IsHeader) continue;

			number++;
			if (i < from || i >= to) continue;

			if (first == 0) first = number;
			last = number;
		}

		return (first, last);
	}

	/// <summary>Occurs when the operator opens the selected item with Enter, a double click or the mouse.</summary>
	public event Action<T>? Activated;

	/// <summary>Gets or sets a hook that sees every key first and returns whether it used the key.</summary>
	public Func<KeyEventArgs, bool>? KeyHook { get; set; }

	/// <summary>Occurs when the selection reaches the last rows, so that the next page can be loaded.</summary>
	public event Action? NearEnd;

	/// <summary>Replaces the groups, keeping the selected item when it is still there.</summary>
	/// <param name="sections">The groups; an empty one still shows its title.</param>
	public void SetSections(IReadOnlyList<Section<T>> sections)
	{
		var keep = HasSelection ? _key(_rows[_selected].Item!) : null;
		var rows = new List<Row>();
		foreach (var section in sections)
		{
			rows.Add(new Row(true, $"{section.Title} ({section.Count})", default));
			rows.AddRange(section.Items.Select(item => new Row(false, string.Empty, item)));
		}

		_rows = rows;
		_selected = keep is null ? -1 : rows.FindIndex(row => !row.IsHeader && _key(row.Item!) == keep);
		if (_selected < 0) _selected = rows.FindIndex(row => !row.IsHeader);
		Settle();
		Revision++;
	}

	/// <summary>Selects the item with an id, when it is in the table.</summary>
	/// <param name="id">The id read by the key function.</param>
	/// <returns>Whether the item is in the table.</returns>
	public bool Select(string id)
	{
		var index = _rows.FindIndex(row => !row.IsHeader && _key(row.Item!) == id);
		if (index < 0) return false;

		_selected = index;
		Settle();
		Revision++;
		return true;
	}

	/// <inheritdoc />
	protected override SizeHints MeasureCore(in LayoutConstraints constraints)
	{
		return SizeHints.Flex(new Size(10, 3), new Size(60, 10), new Size(int.MaxValue, int.MaxValue), 1, 1, 1, 1)
			.Normalize();
	}

	/// <inheritdoc />
	protected override void ArrangeCore(in Rectangle finalRect)
	{
		_bodyHeight = Math.Max(finalRect.Height - 2, 0);
		_scroll.SetViewport(finalRect.Width, _bodyHeight);
		_scroll.SetExtent(finalRect.Width, _rows.Count);
		Settle();
	}

	/// <inheritdoc />
	protected override void RenderOverride(CellBuffer buffer)
	{
		var rect = Bounds;
		if (rect.Width <= 0 || rect.Height <= 0 || false) return;

		_ = Revision;
		var theme = GetTheme();
		var plain = Style.None.WithForeground(theme.Foreground ?? Colors.TerminalWhite);
		for (var y = rect.Y; y < rect.Y + rect.Height; y++)
			buffer.WriteText(rect.X, y, new string(' ', rect.Width), plain);

		var widths = Widths(rect.Width);
		buffer.WriteText(rect.X, rect.Y, Line(widths, column => column.Header), Muted(theme).WithTextStyle(TextStyle.Bold));
		if (_rows.Count == 0) return;

		var offset = Math.Clamp(_scroll.OffsetY, 0, _rows.Count - 1);
		var y0 = rect.Y + 1;
		var first = offset;
		if (_rows[offset].IsHeader)
		{
			first = offset;
		}
		else
		{
			var owner = _rows.FindLastIndex(offset, row => row.IsHeader);
			if (owner >= 0) DrawHeader(buffer, rect, y0, _rows[owner].Title, theme);
			y0++;
		}

		var body = rect.Y + rect.Height - y0;
		for (var i = 0; i < body && first + i < _rows.Count; i++)
		{
			var index = first + i;
			var row = _rows[index];
			var y = y0 + i;
			if (row.IsHeader)
			{
				DrawHeader(buffer, rect, y, row.Title, theme);
				continue;
			}

			DrawItem(buffer, rect, y, row.Item!, widths, index == _selected, theme);
		}
	}

	/// <inheritdoc />
	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (KeyHook?.Invoke(e) == true)
		{
			e.Handled = true;
			return;
		}

		var handled = true;
		switch (e.Key)
		{
			case TerminalKey.Up: Move(-1); break;
			case TerminalKey.Down: Move(1); break;
			case TerminalKey.PageUp: Move(-Math.Max(_bodyHeight - 1, 1)); break;
			case TerminalKey.PageDown: Move(Math.Max(_bodyHeight - 1, 1)); break;
			case TerminalKey.Home: MoveTo(_rows.FindIndex(row => !row.IsHeader)); break;
			case TerminalKey.End: MoveTo(_rows.FindLastIndex(row => !row.IsHeader)); break;
			case TerminalKey.Enter when HasSelection: Activated?.Invoke(_rows[_selected].Item!); break;
			default: handled = false; break;
		}

		if (handled) e.Handled = true;
	}

	/// <inheritdoc />
	protected override void OnPointerPressed(PointerEventArgs e)
	{
		var index = RowAt(e.LocalY);
		if (index < 0 || _rows[index].IsHeader) return;

		MoveTo(index);
		if (e.ClickCount >= 2) Activated?.Invoke(_rows[index].Item!);
		e.Handled = true;
	}

	private int RowAt(int localY)
	{
		if (_rows.Count == 0 || localY < 1) return -1;

		var offset = Math.Clamp(_scroll.OffsetY, 0, _rows.Count - 1);
		var stuck = !_rows[offset].IsHeader;
		var line = localY - 1 - (stuck ? 1 : 0);
		if (line < 0) return -1;

		var index = offset + line;
		return index < _rows.Count ? index : -1;
	}

	private void Move(int delta)
	{
		if (_rows.Count == 0) return;

		var step = Math.Sign(delta);
		var target = _selected < 0 ? 0 : _selected;
		var remaining = Math.Abs(delta);
		while (remaining > 0)
		{
			var next = target + step;
			while (next >= 0 && next < _rows.Count && _rows[next].IsHeader) next += step;
			if (next < 0 || next >= _rows.Count) break;

			target = next;
			remaining--;
		}

		MoveTo(target);
	}

	private void MoveTo(int index)
	{
		if (index < 0 || index >= _rows.Count) return;

		_selected = index;
		Settle();
		Revision++;
		if (index >= _rows.Count - 3) NearEnd?.Invoke();
	}

	/// <summary>Scrolls so that the selected row is inside the view, below the title that stays on top.</summary>
	private void Settle()
	{
		if (_selected < 0 || _selected >= _rows.Count || _bodyHeight <= 0) return;

		var offset = _scroll.OffsetY;
		var capacity = Math.Max(_bodyHeight - 1, 1);
		if (_selected < offset) offset = _selected;
		else if (_selected >= offset + capacity) offset = _selected - capacity + 1;

		offset = Math.Clamp(offset, 0, Math.Max(_rows.Count - 1, 0));
		if (offset != _scroll.OffsetY) _scroll.SetOffset(0, offset);
	}

	private int[] Widths(int total)
	{
		var shown = _columns.Count;
		int Needed(int count) => _columns.Take(count).Sum(c => c.Width) + 2 * Math.Max(count - 1, 0) + 1;
		while (shown > 2 && Needed(shown) > total) shown--;

		var widths = _columns.Take(shown).Select(c => c.Width).ToArray();
		var star = Array.IndexOf(widths, 0);
		if (star >= 0) widths[star] = Math.Max(total - (Needed(shown) - 1) - 1, 4);
		return widths;
	}

	private string Line(int[] widths, Func<Column<T>, string> read)
	{
		var builder = new StringBuilder(" ");
		for (var i = 0; i < widths.Length; i++)
		{
			builder.Append(Fit(read(_columns[i]), widths[i]));
			if (i < widths.Length - 1) builder.Append("  ");
		}

		return builder.ToString();
	}

	private void DrawHeader(CellBuffer buffer, Rectangle rect, int y, string title, Theme theme)
	{
		var rule = _glyphs.Ascii ? '-' : '─';
		var text = $"{rule}{rule} {Palette.Plain(title)} ";
		var line = text + new string(rule, Math.Max(rect.Width - text.Length, 0));
		buffer.WriteText(rect.X, y, line.Length > rect.Width ? line[..rect.Width] : line, Muted(theme));
	}

	private void DrawItem(CellBuffer buffer, Rectangle rect, int y, T item, int[] widths, bool selected, Theme theme)
	{
		var x = rect.X;
		var mark = selected ? (_glyphs.Ascii ? ">" : "▌") : " ";
		var rowStyle = selected && !_noColor
			? Style.None.WithForeground(theme.Foreground ?? Colors.TerminalWhite).WithBackground(theme.Selection ?? Colors.TerminalBlue)
			: Style.None.WithForeground(theme.Foreground ?? Colors.TerminalWhite);
		if (selected && _noColor) rowStyle = rowStyle.WithTextStyle(TextStyle.Invert);
		buffer.WriteText(x, y, selected ? new string(' ', rect.Width) : string.Empty, rowStyle);
		buffer.WriteText(x, y, mark, rowStyle);
		x++;
		for (var i = 0; i < widths.Length; i++)
		{
			var column = _columns[i];
			var style = column.Kind is null || selected ? rowStyle : InkStyle(theme, column.Kind(item));
			buffer.WriteText(x, y, Fit(column.Text(item), widths[i]), style);
			x += widths[i] + 2;
		}
	}

	private Style Muted(Theme theme)
	{
		return _noColor
			? Style.None.WithTextStyle(TextStyle.Dim)
			: Style.None.WithForeground(theme.Muted ?? Colors.TerminalBrightBlack);
	}

	private Style InkStyle(Theme theme, Ink kind)
	{
		var ink = InkTable.Basic(kind);
		if (_noColor)
			return ink switch
			{
				Ink.Muted => Style.None.WithTextStyle(TextStyle.Dim),
				Ink.Accent or Ink.Error => Style.None.WithTextStyle(TextStyle.Bold),
				_ => Style.None
			};

		var color = ink switch
		{
			Ink.Muted => theme.Muted,
			Ink.Accent => theme.Accent ?? theme.Primary,
			Ink.Success => theme.Success,
			Ink.Warning => theme.Warning,
			Ink.Error => theme.Error,
			Ink.Info => theme.Primary,
			_ => theme.Foreground
		};
		return Style.None.WithForeground(color ?? theme.Foreground ?? Colors.TerminalWhite);
	}

	private static string Fit(string text, int width)
	{
		text = Palette.Plain(text);
		if (width <= 0) return string.Empty;

		return text.Length <= width ? text.PadRight(width) : text[..Math.Max(width - 1, 0)] + "…";
	}

	private readonly record struct Row(bool IsHeader, string Title, T? Item);
}
