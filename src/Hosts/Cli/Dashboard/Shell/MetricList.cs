using Basil.Host.Cli.Dashboard.Data;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Scrolling;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>
///     The measurements of one category: a label on the left and its value right-aligned, with a foldable title for
///     each group. One row at a time is selected; inside a <see cref="XenoAtom.Terminal.UI.Controls.ScrollViewer" /> it
///     scrolls with a bar of its own.
/// </summary>
internal sealed partial class MetricList : Visual, IScrollable
{
	/// <summary>Counts the changes of the content, which the framework tracks to redraw the list.</summary>
	[Bindable] public partial int Revision { get; set; }

	private readonly Glyphs _glyphs;
	private readonly bool _noColor;
	private readonly ScrollModel _scroll;
	private readonly HashSet<string> _closed = [];
	private IReadOnlyList<MetricLine> _lines = [];
	private List<Row> _rows = [];
	private string? _selectedKey;
	private int _cursor = -1;
	private int _bodyHeight;

	/// <summary>Creates the list.</summary>
	/// <param name="glyphs">The characters to draw with.</param>
	/// <param name="noColor">Whether the list is monochrome.</param>
	public MetricList(Glyphs glyphs, bool noColor)
	{
		_glyphs = glyphs;
		_noColor = noColor;
		_scroll = new ScrollModel(this);
		Focusable = true;
		IsTabStop = true;
		HorizontalAlignment = Align.Stretch;
		VerticalAlignment = Align.Stretch;
	}

	/// <inheritdoc />
	public ScrollModel Scroll => _scroll;

	/// <summary>Occurs when the operator picks a measurement; the flag tells whether it was by Enter or a click.</summary>
	public event Action<string, bool>? Picked;

	/// <summary>Occurs when the operator presses Escape in the list.</summary>
	public event Action? Dismissed;

	/// <summary>Sets which measurement is highlighted, or <see langword="null" /> for none of this list.</summary>
	/// <param name="key">The key of the selected measurement.</param>
	public void Select(string? key)
	{
		if (key == _selectedKey) return;

		_selectedKey = key;
		var at = _rows.FindIndex(row => row.Line?.Key == key);
		if (at >= 0)
		{
			_cursor = at;
			Settle();
		}

		Revision++;
	}

	/// <summary>Replaces the measurements, keeping the fold state and the cursor.</summary>
	/// <param name="lines">The measurements; the ones with a group are listed after those without, under a title.</param>
	public void SetLines(IReadOnlyList<MetricLine> lines)
	{
		var keep = _cursor >= 0 && _cursor < _rows.Count ? _rows[_cursor] : default;
		_lines = lines;
		Rebuild();
		_cursor = keep.IsTitle ? _rows.FindIndex(row => row.IsTitle && row.Group == keep.Group)
			: keep.Line is null ? -1 : _rows.FindIndex(row => row.Line?.Key == keep.Line.Key);
		if (_cursor < 0 && _rows.Count > 0) _cursor = Math.Min(Math.Max(_cursor, 0), _rows.Count - 1);
		Revision++;
	}

	private void Rebuild()
	{
		var rows = _lines.Where(line => line.Group.Length == 0).Select(line => new Row(false, string.Empty, 0, true, line)).ToList();
		foreach (var group in _lines.Where(line => line.Group.Length > 0).GroupBy(line => line.Group))
		{
			var open = !_closed.Contains(group.Key);
			rows.Add(new Row(true, group.Key, group.Count(), open, null));
			if (open) rows.AddRange(group.Select(line => new Row(false, string.Empty, 0, true, line)));
		}

		_rows = rows;
		_scroll.SetExtent(_scroll.ViewportWidth, _rows.Count);
	}

	/// <inheritdoc />
	protected override SizeHints MeasureCore(in LayoutConstraints constraints)
	{
		return SizeHints.Flex(new Size(8, 2), new Size(24, 6), new Size(int.MaxValue, int.MaxValue), 1, 1, 1, 1).Normalize();
	}

	/// <inheritdoc />
	protected override void ArrangeCore(in Rectangle finalRect)
	{
		_bodyHeight = finalRect.Height;
		_scroll.SetViewport(finalRect.Width, _bodyHeight);
		_scroll.SetExtent(finalRect.Width, _rows.Count);
		Settle();
	}

	/// <inheritdoc />
	protected override void RenderOverride(CellBuffer buffer)
	{
		var rect = Bounds;
		if (rect.Width <= 0 || rect.Height <= 0) return;

		_ = Revision;
		var theme = GetTheme();
		var plain = Style.None.WithForeground(theme.Foreground ?? Colors.TerminalWhite);
		for (var y = rect.Y; y < rect.Y + rect.Height; y++) buffer.WriteText(rect.X, y, new string(' ', rect.Width), plain);

		var offset = Math.Clamp(_scroll.OffsetY, 0, Math.Max(_rows.Count - 1, 0));
		for (var i = 0; i < rect.Height && offset + i < _rows.Count; i++)
		{
			var row = _rows[offset + i];
			if (row.IsTitle) DrawTitle(buffer, rect, rect.Y + i, row, offset + i == _cursor && HasFocus, theme);
			else DrawLine(buffer, rect, rect.Y + i, row.Line!, row.Line!.Key == _selectedKey, theme);
		}
	}

	private void DrawTitle(CellBuffer buffer, Rectangle rect, int y, Row row, bool selected, Theme theme)
	{
		var muted = Muted(theme);
		var bold = (_noColor ? Style.None : Style.None.WithForeground(theme.Foreground ?? Colors.TerminalWhite))
			.WithTextStyle(TextStyle.Bold);
		if (selected)
		{
			muted = Select(muted, theme);
			bold = Select(bold, theme);
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

		var rule = _glyphs.Rule;
		Put($"{(row.Open ? _glyphs.Open : _glyphs.Folded)} {rule}{rule} ", muted);
		Put(Palette.Plain(row.Group), bold);
		Put($" ({row.Count}) ", muted);
		Put(new string(rule, Math.Max(rect.X + rect.Width - x, 0)), muted);
	}

	private void DrawLine(CellBuffer buffer, Rectangle rect, int y, MetricLine line, bool selected, Theme theme)
	{
		var label = Palette.Plain(line.Label);
		var value = Palette.Plain(line.Value);
		var room = Math.Max(rect.Width - 2, 1);
		// The value keeps at least the room of a short label; the label gives way to the rest.
		var valueRoom = Math.Max(room - Math.Min(label.Length, 12) - 1, 1);
		if (value.Length > valueRoom) value = value[..Math.Max(valueRoom - 1, 0)] + "…";

		var labelRoom = Math.Max(room - value.Length - 1, 1);
		if (label.Length > labelRoom) label = label[..Math.Max(labelRoom - 1, 0)] + "…";

		var labelStyle = Muted(theme);
		var valueStyle = InkStyle(theme, line.Ink);
		var markStyle = Style.None.WithForeground(theme.Foreground ?? Colors.TerminalWhite);
		if (selected)
		{
			buffer.WriteText(rect.X, y, new string(' ', rect.Width), Select(markStyle, theme));
			labelStyle = Select(labelStyle, theme);
			valueStyle = Select(valueStyle, theme);
			markStyle = Select(markStyle, theme);
		}

		buffer.WriteText(rect.X, y, selected ? (_glyphs.Ascii ? ">" : "▌") : " ", markStyle);
		buffer.WriteText(rect.X + 1, y, label, labelStyle);
		buffer.WriteText(rect.X + rect.Width - 1 - value.Length, y, value, valueStyle);
	}

	/// <inheritdoc />
	protected override void OnKeyDown(KeyEventArgs e)
	{
		var handled = true;
		switch (e.Key)
		{
			case TerminalKey.Up: Move(-1); break;
			case TerminalKey.Down: Move(1); break;
			case TerminalKey.PageUp: Move(-Math.Max(_bodyHeight - 1, 1)); break;
			case TerminalKey.PageDown: Move(Math.Max(_bodyHeight - 1, 1)); break;
			case TerminalKey.Home: MoveTo(0); break;
			case TerminalKey.End: MoveTo(_rows.Count - 1); break;
			case TerminalKey.Enter when OnTitle: Fold(_cursor, null); break;
			case TerminalKey.Enter when _cursor >= 0 && _cursor < _rows.Count: Picked?.Invoke(_rows[_cursor].Line!.Key, true); break;
			case TerminalKey.Escape: Dismissed?.Invoke(); break;
			case TerminalKey.Left when OnTitle: Fold(_cursor, false); break;
			case TerminalKey.Right when OnTitle: Fold(_cursor, true); break;
			default: handled = false; break;
		}

		if (handled) e.Handled = true;
	}

	private bool OnTitle => _cursor >= 0 && _cursor < _rows.Count && _rows[_cursor].IsTitle;

	/// <inheritdoc />
	protected override void OnPointerPressed(PointerEventArgs e)
	{
		if (e.RoutingPhase != RoutingPhase.Bubble || e.Button != TerminalMouseButton.Left) return;

		var index = _scroll.OffsetY + e.LocalY;
		if (index < 0 || index >= _rows.Count) return;

		_cursor = index;
		e.Handled = true;
		if (_rows[index].IsTitle) Fold(index, null);
		else Picked?.Invoke(_rows[index].Line!.Key, true);
		Revision++;
	}

	private void Fold(int index, bool? open)
	{
		var row = _rows[index];
		if (open is { } wanted && wanted == row.Open) return;

		if (!_closed.Remove(row.Group)) _closed.Add(row.Group);
		Rebuild();
		_cursor = _rows.FindIndex(r => r.IsTitle && r.Group == row.Group);
		Settle();
		Revision++;
	}

	private void Move(int delta)
	{
		if (_rows.Count > 0) MoveTo(Math.Clamp((_cursor < 0 ? 0 : _cursor) + delta, 0, _rows.Count - 1));
	}

	private void MoveTo(int index)
	{
		if (index < 0 || index >= _rows.Count) return;

		_cursor = index;
		Settle();
		Revision++;
		if (_rows[index].Line is { } line) Picked?.Invoke(line.Key, false);
	}

	private void Settle()
	{
		if (_cursor < 0 || _cursor >= _rows.Count || _bodyHeight <= 0) return;

		var offset = _scroll.OffsetY;
		if (_cursor < offset) offset = _cursor;
		else if (_cursor >= offset + _bodyHeight) offset = _cursor - _bodyHeight + 1;

		offset = Math.Clamp(offset, 0, Math.Max(_rows.Count - _bodyHeight, 0));
		if (offset != _scroll.OffsetY) _scroll.SetOffset(0, offset);
	}

	private Style Select(Style style, Theme theme)
	{
		return _noColor
			? style.WithTextStyle(TextStyle.Invert)
			: style.WithBackground(theme.Selection ?? Colors.TerminalBlue);
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

	private readonly record struct Row(bool IsTitle, string Group, int Count, bool Open, MetricLine? Line);
}
