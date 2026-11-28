using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal static class PlaceholderTab
{
	public static Visual Create(string name)
	{
		return new Padder(new TextBlock($"{name}: not built yet.")).Padding(new Thickness(1));
	}
}