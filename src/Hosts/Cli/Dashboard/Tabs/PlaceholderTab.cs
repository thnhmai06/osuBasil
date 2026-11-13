using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>Stands in for a tab that is not built yet.</summary>
internal static class PlaceholderTab
{
	/// <summary>Creates the view.</summary>
	public static Visual Create(string name)
	{
		return new Padder(new TextBlock($"{name}: not built yet.")).Padding(new Thickness(1));
	}
}
