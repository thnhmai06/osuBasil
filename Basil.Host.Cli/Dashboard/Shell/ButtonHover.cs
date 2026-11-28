using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

internal static class ButtonHover
{
	public static void Apply(Visual root, Theme theme)
	{
		root.SetStyle(Theme.Key, theme);
		root.SetStyle(ButtonStyle.Key, ButtonStyle.Default with { ShowBorder = false, Hovered = Hovered(theme) });
	}

	public static Style Hovered(Theme theme)
	{
		return theme.ControlFillHover is { } fill
			? Style.None.WithBackground(fill)
			: Style.None.WithTextStyle(TextStyle.Invert);
	}
}