using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed partial class HoverLabel : Visual
{
	[Bindable] private partial bool Hot { get; set; }

	private readonly string _text;
	private readonly Action? _click;

	public HoverLabel(string text, Action? click)
	{
		_text = text;
		_click = click;
		HorizontalAlignment = Align.Start;
	}

	protected override SizeHints MeasureCore(in LayoutConstraints constraints)
	{
		return SizeHints.Fixed(new Size(_text.Length, 1));
	}

	protected override void RenderOverride(CellBuffer buffer)
	{
		var rect = Bounds;
		if (rect.Width <= 0 || rect.Height <= 0) return;

		_ = Hot;
		var theme = GetTheme();
		var style = Style.None.WithForeground(theme.Muted ?? Colors.TerminalBrightBlack);
		if (_click is not null && IsHovered) style |= ButtonHover.Hovered(theme);
		buffer.WriteText(rect.X, rect.Y, _text.Length > rect.Width ? _text[..rect.Width] : _text, style);
	}

	protected override void OnHoveredChanged(bool isHovered)
	{
		Hot = isHovered;
	}

	protected override void OnPointerPressed(PointerEventArgs e)
	{
		if (_click is null || e.Button != TerminalMouseButton.Left) return;

		e.Handled = true;
		_click();
	}
}
