using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Spreads the tabs of a strip evenly over its whole row, with the same space on both sides of every label.</summary>
internal sealed class TabSpread
{
	private readonly TabControl _strip;
	private readonly TabControlStyle _style;
	private readonly int[] _labels;
	private int _width = -1;

	/// <summary>Creates the spreading of one strip.</summary>
	/// <param name="strip">The strip.</param>
	/// <param name="style">The style of the strip, whose tab padding is replaced.</param>
	/// <param name="labels">The text of each tab, in order.</param>
	public TabSpread(TabControl strip, TabControlStyle style, IEnumerable<string> labels)
	{
		_strip = strip;
		_style = style;
		_labels = [.. labels.Select(label => label.Length)];
	}

	/// <summary>Gets the padding on each side of a label that fills a row; one column when the row is too narrow.</summary>
	/// <param name="width">The columns of the row.</param>
	/// <param name="labels">The width of each label.</param>
	/// <returns>The columns of padding before and after every label.</returns>
	public static (int Before, int After) Padding(int width, IReadOnlyList<int> labels)
	{
		// Every tab takes its label, its padding and one column that separates it from the next.
		var free = width - labels.Sum() - labels.Count;
		var each = labels.Count == 0 ? 0 : free / labels.Count;
		return each < 2 ? (0, 1) : (each / 2, each - each / 2);
	}

	/// <summary>Spreads the tabs again when the row changed width.</summary>
	/// <param name="width">The columns of the row.</param>
	public void Fit(int width)
	{
		if (width <= 0 || width == _width) return;

		_width = width;
		var (before, after) = Padding(width, _labels);
		_strip.Style(_style with { TabPadding = new Thickness(before, 0, after, 0) });
	}
}