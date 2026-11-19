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

internal sealed record Column<T>(string Header, int Width, Func<T, string> Text, Func<T, Ink>? Kind = null)
{
	public int Weight { get; init; } = 1;

	public bool Right { get; init; }

	public Func<T, (string Title, Func<Task<DetailView>> Read)?>? Link { get; init; }
}

internal sealed record Section<T>(string Title, string Count, IReadOnlyList<T> Items);

internal sealed partial class GroupedList<T> : Visual, IScrollable
{
	// A shared-width column is worth showing only from this width; a narrower table drops its last columns instead.
	private const int MinShared = 10;
	private readonly HashSet<string> _closed = [];
	private readonly IReadOnlyList<Column<T>> _columns;
	private readonly Glyphs _glyphs;
	private readonly Func<T, string> _key;
	private readonly bool _noColor;
	private readonly HashSet<string> _searchClosed = [];
	private readonly WheelAcceleration _wheel;
	private int _bodyHeight;
	private List<Row> _rows = [];
	private bool _searching;
	private IReadOnlyList<Section<T>> _sections = [];
	private int _selected = -1;

	public GroupedList(IReadOnlyList<Column<T>> columns, Func<T, string> key, Glyphs glyphs, bool noColor,
		TimeProvider clock)
	{
		_wheel = new WheelAcceleration(clock, 3);
		_columns = columns;
		_key = key;
		_glyphs = glyphs;
		_noColor = noColor;
		Scroll = new ScrollModel(this);
		Focusable = true;
		HorizontalAlignment = Align.Stretch;
		VerticalAlignment = Align.Stretch;
		IsTabStop = true;
	}

	[Bindable] public partial int Revision { get; set; }

	public T? Selected => HasSelection ? _rows[_selected].Item : default;

	public bool HasSelection => _selected >= 0 && _selected < _rows.Count && !_rows[_selected].IsHeader;

	public bool AtFirst => _selected <= 0;

	public int ItemCount => _rows.Count(row => !row.IsHeader);

	public int ActivatedColumn { get; private set; } = -1;

	public Func<KeyEventArgs, bool>? KeyHook { get; set; }

	private HashSet<string> Folded => _searching ? _searchClosed : _closed;

	private bool OnTitle => _selected >= 0 && _selected < _rows.Count && _rows[_selected].IsHeader;

	public ScrollModel Scroll { get; }

	public (int First, int Last) VisibleItems()
	{
		var first = 0;
		var last = 0;
		var number = 0;
		var from = Scroll.OffsetY;
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

	public event Action<T>? Activated;

	public event Action? Moved;

	public event Action? NearEnd;

	public void SetSections(IReadOnlyList<Section<T>> sections, bool searching = false)
	{
		var keepItem = HasSelection ? _key(_rows[_selected].Item!) : null;
		var keepGroup = _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Title : null;
		if (searching != _searching)
		{
			_searching = searching;
			_searchClosed.Clear();
		}

		_sections = sections;
		Rebuild();
		_selected = keepItem is null ? -1 : _rows.FindIndex(row => !row.IsHeader && _key(row.Item!) == keepItem);
		if (_selected < 0 && keepGroup is not null)
			_selected = _rows.FindIndex(row => row.IsHeader && row.Title == keepGroup);
		if (_selected < 0) _selected = _rows.FindIndex(row => !row.IsHeader);
		if (_selected < 0) _selected = _rows.Count > 0 ? 0 : -1;
		Settle();
		Revision++;
	}

	public bool TryFind(string id, out T item)
	{
		item = _sections.SelectMany(section => section.Items).FirstOrDefault(candidate => _key(candidate) == id)!;
		return item is not null;
	}

	public void SelectFirst()
	{
		var first = _rows.FindIndex(row => !row.IsHeader);
		MoveTo(first < 0 ? 0 : first);
	}

	public bool Select(string id)
	{
		var owner = _sections.FirstOrDefault(section => section.Items.Any(item => _key(item) == id));
		if (owner is null) return false;

		if (Folded.Remove(owner.Title)) Rebuild();
		_selected = _rows.FindIndex(row => !row.IsHeader && _key(row.Item!) == id);
		Settle();
		Revision++;
		return true;
	}

	private void Rebuild()
	{
		var rows = new List<Row>();
		foreach (var section in _sections)
		{
			if (_searching && section.Items.Count == 0) continue;

			var open = !Folded.Contains(section.Title);
			rows.Add(new Row(true, section.Title, section.Count, open, default));
			if (open)
				rows.AddRange(section.Items.Select(item => new Row(false, string.Empty, string.Empty, true, item)));
		}

		_rows = rows;
	}

	private void Fold(int index, bool? open = null)
	{
		var row = _rows[index];
		if (open is { } wanted && wanted == row.Open) return;

		if (!Folded.Remove(row.Title)) Folded.Add(row.Title);
		Rebuild();
		_selected = _rows.FindIndex(r => r.IsHeader && r.Title == row.Title);
		Settle();
		Revision++;
		Moved?.Invoke();
	}

	protected override SizeHints MeasureCore(in LayoutConstraints constraints)
	{
		return SizeHints.Flex(new Size(10, 3), new Size(60, 10), new Size(int.MaxValue, int.MaxValue), 1, 1, 1, 1)
			.Normalize();
	}

	protected override void ArrangeCore(in Rectangle finalRect)
	{
		_bodyHeight = Math.Max(finalRect.Height - 2, 0);
		Scroll.SetViewport(finalRect.Width, _bodyHeight);
		Scroll.SetExtent(finalRect.Width, _rows.Count);
		Settle();
	}

	protected override void RenderOverride(CellBuffer buffer)
	{
		var rect = Bounds;
		if (rect.Width <= 0 || rect.Height <= 0) return;

		_ = Revision;
		var theme = GetTheme();
		var plain = Style.None.WithForeground(theme.Foreground ?? Colors.TerminalWhite);
		for (var y = rect.Y; y < rect.Y + rect.Height; y++)
			buffer.WriteText(rect.X, y, new string(' ', rect.Width), plain);

		var widths = Widths(rect.Width);
		buffer.WriteText(rect.X, rect.Y, Line(widths, column => column.Header),
			Muted(theme).WithTextStyle(TextStyle.Bold));
		if (_rows.Count == 0) return;

		var offset = Math.Clamp(Scroll.OffsetY, 0, _rows.Count - 1);
		var y0 = rect.Y + 1;
		var first = offset;
		if (_rows[offset].IsHeader)
		{
			first = offset;
		}
		else
		{
			var owner = _rows.FindLastIndex(offset, row => row.IsHeader);
			if (owner >= 0) DrawHeader(buffer, rect, y0, _rows[owner], owner == _selected, theme);
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
				DrawHeader(buffer, rect, y, row, index == _selected, theme);
				continue;
			}

			DrawItem(buffer, rect, y, row.Item!, widths, index == _selected, theme);
		}
	}

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
			case TerminalKey.Home: MoveTo(0); break;
			case TerminalKey.End: MoveTo(_rows.Count - 1); break;
			case TerminalKey.Enter when HasSelection:
				ActivatedColumn = -1;
				Activated?.Invoke(_rows[_selected].Item!);
				break;
			case TerminalKey.Enter when OnTitle: Fold(_selected); break;
			case TerminalKey.Left when OnTitle: Fold(_selected, false); break;
			case TerminalKey.Right when OnTitle: Fold(_selected, true); break;
			default: handled = false; break;
		}

		if (handled) e.Handled = true;
	}

	protected override void OnPointerPressed(PointerEventArgs e)
	{
		if (e.RoutingPhase != RoutingPhase.Bubble || e.Button != TerminalMouseButton.Left) return;

		var index = RowAt(e.LocalY);
		if (index < 0) return;

		if (_rows[index].IsHeader)
		{
			Fold(index);
			e.Handled = true;
			return;
		}

		// A click selects; a double click opens.
		MoveTo(index);
		if (e.ClickCount >= 2)
		{
			ActivatedColumn = ColumnAt(e.LocalX);
			Activated?.Invoke(_rows[index].Item!);
		}

		e.Handled = true;
	}

	protected override void OnPointerWheel(PointerEventArgs e)
	{
		if (e.RoutingPhase != RoutingPhase.Bubble || e.WheelDelta == 0) return;

		var step = _wheel.Step();
		Move(e.WheelDelta > 0 ? -step : step);
		e.Handled = true;
	}

	private int RowAt(int localY)
	{
		if (_rows.Count == 0 || localY < 1) return -1;

		var offset = Math.Clamp(Scroll.OffsetY, 0, _rows.Count - 1);
		var stuck = !_rows[offset].IsHeader;
		if (stuck && localY == 1) return _rows.FindLastIndex(offset, row => row.IsHeader);

		var line = localY - 1 - (stuck ? 1 : 0);
		if (line < 0) return -1;

		var index = offset + line;
		return index < _rows.Count ? index : -1;
	}

	private void Move(int delta)
	{
		if (_rows.Count > 0) MoveTo(Math.Clamp((_selected < 0 ? 0 : _selected) + delta, 0, _rows.Count - 1));
	}

	private void MoveTo(int index)
	{
		if (index < 0 || index >= _rows.Count) return;

		_selected = index;
		Settle();
		Revision++;
		Moved?.Invoke();
		if (index >= _rows.Count - 3) NearEnd?.Invoke();
	}

	private void Settle()
	{
		if (_selected < 0 || _selected >= _rows.Count || _bodyHeight <= 0) return;

		var offset = Scroll.OffsetY;
		var capacity = Math.Max(_bodyHeight - 1, 1);
		if (_selected < offset) offset = _selected;
		else if (_selected >= offset + capacity) offset = _selected - capacity + 1;

		offset = Math.Clamp(offset, 0, Math.Max(_rows.Count - 1, 0));
		if (offset != Scroll.OffsetY) Scroll.SetOffset(0, offset);
	}

	private int ColumnAt(int localX)
	{
		var x = 1;
		var widths = Widths(Bounds.Width);
		for (var i = 0; i < widths.Length; i++)
		{
			if (localX < x + widths[i]) return i;

			x += widths[i] + 2;
		}

		return -1;
	}

	// The columns of width 0 share what the fixed ones leave, each by its weight, so no long text starves the others.
	private int[] Widths(int total)
	{
		var shown = _columns.Count;

		int Fixed(int count)
		{
			return _columns.Take(count).Sum(c => c.Width) + 2 * Math.Max(count - 1, 0) + 1;
		}

		int Needed(int count)
		{
			return Fixed(count) + MinShared * _columns.Take(count).Count(c => c.Width == 0);
		}

		while (shown > 2 && Needed(shown) > total) shown--;

		var widths = _columns.Take(shown).Select(c => c.Width).ToArray();
		var stars = Enumerable.Range(0, shown).Where(i => widths[i] == 0).ToList();
		if (stars.Count == 0) return widths;

		var pool = Math.Max(total - Fixed(shown), 4 * stars.Count);
		var weights = stars.Sum(i => _columns[i].Weight);
		var left = pool;
		foreach (var i in stars)
		{
			widths[i] = i == stars[^1] ? Math.Max(left, 4) : Math.Max(pool * _columns[i].Weight / weights, 4);
			left -= widths[i];
		}

		return widths;
	}

	private string Line(int[] widths, Func<Column<T>, string> read)
	{
		var builder = new StringBuilder(" ");
		for (var i = 0; i < widths.Length; i++)
		{
			builder.Append(Fit(read(_columns[i]), widths[i], _columns[i].Right));
			if (i < widths.Length - 1) builder.Append("  ");
		}

		return builder.ToString();
	}

	private void DrawHeader(CellBuffer buffer, Rectangle rect, int y, Row row, bool selected, Theme theme)
	{
		GroupTitle.Draw(buffer, rect, y, _glyphs, _noColor, theme, row.Title, row.Count, row.Open, selected);
	}

	private void DrawItem(CellBuffer buffer, Rectangle rect, int y, T item, int[] widths, bool selected, Theme theme)
	{
		var x = rect.X;
		var mark = selected ? _glyphs.Ascii ? ">" : "▌" : " ";
		var rowStyle = selected && !_noColor
			? Style.None.WithForeground(theme.Foreground ?? Colors.TerminalWhite)
				.WithBackground(theme.Selection ?? Colors.TerminalBlue)
			: Style.None.WithForeground(theme.Foreground ?? Colors.TerminalWhite);
		if (selected && _noColor) rowStyle = rowStyle.WithTextStyle(TextStyle.Invert);
		buffer.WriteText(x, y, selected ? new string(' ', rect.Width) : string.Empty, rowStyle);
		buffer.WriteText(x, y, mark, rowStyle);
		x++;
		for (var i = 0; i < widths.Length; i++)
		{
			var column = _columns[i];
			var style = column.Kind is null || selected ? rowStyle : InkStyle(theme, column.Kind(item));
			buffer.WriteText(x, y, Fit(column.Text(item), widths[i], column.Right), style);
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
		return InkStyles.Of(theme, kind, _noColor);
	}

	private static string Fit(string text, int width, bool right = false)
	{
		text = Palette.Plain(text);
		if (width <= 0) return string.Empty;

		if (text.Length > width) return text[..Math.Max(width - 1, 0)] + "…";

		return right ? text.PadLeft(width) : text.PadRight(width);
	}

	private readonly record struct Row(bool IsHeader, string Title, string Count, bool Open, T? Item);
}