using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed class TabSpread
{
	private readonly TabControl _strip;
	private readonly TabControlStyle _style;
	private readonly int[] _labels;
	private int _width = -1;

	public TabSpread(TabControl strip, TabControlStyle style, IEnumerable<string> labels)
	{
		_strip = strip;
		_style = style;
		_labels = [.. labels.Select(label => label.Length)];
	}

	public static (int Before, int After) Padding(int width, IReadOnlyList<int> labels)
	{
		// Every tab takes its label, its padding and one column that separates it from the next.
		var free = width - labels.Sum() - labels.Count;
		var each = labels.Count == 0 ? 0 : free / labels.Count;
		return each < 2 ? (0, 1) : (each / 2, each - each / 2);
	}

	public void Fit(int width)
	{
		if (width <= 0 || width == _width) return;

		_width = width;
		var (before, after) = Padding(width, _labels);
		_strip.Style(_style with { TabPadding = new Thickness(before, 0, after, 0) });
	}
}