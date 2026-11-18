using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed partial class FoldTitle : Visual
{
	private readonly Glyphs _glyphs;
	private readonly bool _noColor;
	private readonly string _name;
	private readonly int _indent;

	public FoldTitle(string name, Glyphs glyphs, bool noColor, int indent = 0)
	{
		_name = name;
		_indent = indent;
		_glyphs = glyphs;
		_noColor = noColor;
		Focusable = true;
		IsTabStop = true;
		HorizontalAlignment = Align.Stretch;
	}

	[Bindable] public partial bool Open { get; set; }

	[Bindable] public partial int Count { get; set; }

	public event Action<bool>? Folding;

	protected override SizeHints MeasureCore(in LayoutConstraints constraints)
	{
		return SizeHints.FlexX(new Size(4, 1), new Size(20, 1), 1, 1).Normalize();
	}

	protected override void RenderOverride(CellBuffer buffer)
	{
		var rect = Bounds;
		if (rect.Width <= 0 || rect.Height <= 0) return;

		if (_indent > 0) rect = new Rectangle(rect.X + _indent, rect.Y, Math.Max(rect.Width - _indent, 0), rect.Height);
		GroupTitle.Draw(buffer, rect, rect.Y, _glyphs, _noColor, GetTheme(), _name, Count.ToString(), Open, HasFocus);
	}

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

	protected override void OnPointerPressed(PointerEventArgs e)
	{
		if (e.RoutingPhase != RoutingPhase.Bubble || e.Button != TerminalMouseButton.Left) return;

		Folding?.Invoke(!Open);
		e.Handled = true;
	}
}
