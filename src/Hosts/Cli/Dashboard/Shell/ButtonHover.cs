using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Gives every button of the dashboard the hover look of a tab header: a lighter background, the same text colour.</summary>
internal static class ButtonHover
{
	/// <summary>Sets the theme and the button style of a root, so that all buttons under it highlight on hover.</summary>
	/// <param name="root">The root of the interface.</param>
	/// <param name="theme">The theme in use.</param>
	public static void Apply(Visual root, Theme theme)
	{
		root.SetStyle(Theme.Key, theme);
		root.SetStyle(ButtonStyle.Key, ButtonStyle.Default with { ShowBorder = false, Hovered = Hovered(theme) });
	}

	/// <summary>Gets the style of something the pointer is over: the hover background of the theme, nothing else.</summary>
	/// <param name="theme">The theme in use.</param>
	public static Style Hovered(Theme theme)
	{
		return theme.ControlFillHover is { } fill
			? Style.None.WithBackground(fill)
			: Style.None.WithTextStyle(TextStyle.Invert);
	}
}
