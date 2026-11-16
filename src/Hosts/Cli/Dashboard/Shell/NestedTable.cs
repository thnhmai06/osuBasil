using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Builds the table that stands in the detail frame in place of its fields.</summary>
internal static class NestedTable
{
	/// <summary>Makes a table whose double click or Enter hands the item to a callback.</summary>
	/// <param name="columns">The columns.</param>
	/// <param name="key">Reads the id of an item.</param>
	/// <param name="sections">The groups of items.</param>
	/// <param name="glyphs">The characters to draw with.</param>
	/// <param name="noColor">Whether the table is monochrome.</param>
	/// <param name="clock">Measures the time between the notches of the mouse wheel.</param>
	/// <param name="open">Called with the item the operator opened.</param>
	public static Visual Of<T>(IReadOnlyList<Column<T>> columns, Func<T, string> key, IReadOnlyList<Section<T>> sections,
		Glyphs glyphs, bool noColor, TimeProvider clock, Action<T> open)
	{
		var list = new GroupedList<T>(columns, key, glyphs, noColor, clock);
		list.SetSections(sections);
		list.Activated += open;
		return new ScrollViewer(list).Stretch();
	}
}
