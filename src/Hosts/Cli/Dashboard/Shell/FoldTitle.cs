using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>
///     The one-row title of a group of a form, in the shape of the group titles of the tables (<c>▾ ── Name (4) ───</c>):
///     Enter, Left, Right or a click opens and folds the group.
/// </summary>
internal sealed partial class FoldTitle : Visual
{
	private readonly Glyphs _glyphs;
	private readonly bool _noColor;
	private readonly string _name;

	/// <summary>Creates the title.</summary>
	/// <param name="name">The name of the group; its first letter is shown in capital.</param>
	/// <param name="glyphs">The characters to draw with.</param>
	/// <param name="noColor">Whether the title is monochrome.</param>
	public FoldTitle(string name, Glyphs glyphs, bool noColor)
	{
		_name = name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;
		_glyphs = glyphs;
		_noColor = noColor;
		Focusable = true;
		IsTabStop = true;
		HorizontalAlignment = Align.Stretch;
	}

	/// <summary>Gets or sets whether the group is open.</summary>
	[Bindable] public partial bool Open { get; set; }

	/// <summary>Gets or sets the number shown next to the name.</summary>
	[Bindable] public partial int Count { get; set; }

	/// <summary>Occurs when the operator asks to open (<see langword="true" />) or fold (<see langword="false" />) the group.</summary>
	public event Action<bool>? Folding;

	/// <inheritdoc />
	protected override SizeHints MeasureCore(in LayoutConstraints constraints)
	{
		return SizeHints.FlexX(new Size(4, 1), new Size(20, 1), 1, 1).Normalize();
	}

	/// <inheritdoc />
	protected override void RenderOverride(CellBuffer buffer)
	{
		var rect = Bounds;
		if (rect.Width <= 0 || rect.Height <= 0) return;

		var open = Open;
		var count = Count;
		var theme = GetTheme();
		var rule = _glyphs.Rule;
		var muted = _noColor
			? Style.None.WithTextStyle(TextStyle.Dim)
			: Style.None.WithForeground(theme.Muted ?? Colors.TerminalBrightBlack);
		var bold = (_noColor ? Style.None : Style.None.WithForeground(theme.Foreground ?? Colors.TerminalWhite))
			.WithTextStyle(TextStyle.Bold);
		if (HasFocus)
		{
			muted = Select(muted, theme);
			bold = Select(bold, theme);
			buffer.WriteText(rect.X, rect.Y, new string(' ', rect.Width), muted);
		}

		var x = rect.X;

		void Put(string text, Style style)
		{
			var room = rect.X + rect.Width - x;
			if (room <= 0) return;

			var shown = text.Length > room ? text[..room] : text;
			buffer.WriteText(x, rect.Y, shown, style);
			x += shown.Length;
		}

		Put($"{(open ? _glyphs.Open : _glyphs.Folded)} {rule}{rule} ", muted);
		Put(_name, bold);
		Put($" ({count}) ", muted);
		Put(new string(rule, Math.Max(rect.X + rect.Width - x, 0)), muted);
	}

	/// <inheritdoc />
	protected override void OnKeyDown(KeyEventArgs e)
	{
		switch (e.Key)
		{
			case TerminalKey.Enter: Folding?.Invoke(!Open); break;
			case TerminalKey.Left: Folding?.Invoke(false); break;
			case TerminalKey.Right: Folding?.Invoke(true); break;
			default: return;
		}

		e.Handled = true;
	}

	/// <inheritdoc />
	protected override void OnPointerPressed(PointerEventArgs e)
	{
		if (e.RoutingPhase != RoutingPhase.Bubble || e.Button != TerminalMouseButton.Left) return;

		Folding?.Invoke(!Open);
		e.Handled = true;
	}

	private Style Select(Style style, Theme theme)
	{
		return _noColor
			? style.WithTextStyle(TextStyle.Invert)
			: style.WithBackground(theme.Selection ?? Colors.TerminalBlue);
	}
}
