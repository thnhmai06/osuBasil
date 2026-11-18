using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Basil.Host.Cli.Dashboard.Shell;

internal static class NestedTable
{
	public static Visual Of<T>(IReadOnlyList<Column<T>> columns, Func<T, string> key, IReadOnlyList<Section<T>> sections,
		Glyphs glyphs, bool noColor, TimeProvider clock, Action<T> open)
	{
		var list = new GroupedList<T>(columns, key, glyphs, noColor, clock);
		list.SetSections(sections);
		list.Activated += open;
		return new ScrollViewer(list).Stretch();
	}
}
