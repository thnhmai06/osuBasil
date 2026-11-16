using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Turns a <see cref="Ink" /> into the style the dashboard draws it with; the one place that knows which colour of the theme each kind of text has.</summary>
internal static class InkStyles
{
	/// <summary>Gets the style of a kind of text.</summary>
	/// <param name="theme">The theme in use.</param>
	/// <param name="kind">The kind of text.</param>
	/// <param name="noColor">Whether the dashboard is monochrome, so that kinds differ by weight only.</param>
	public static Style Of(Theme theme, Ink kind, bool noColor)
	{
		var ink = InkTable.Basic(kind);
		if (noColor)
			return ink switch
			{
				Ink.Muted => Style.None.WithTextStyle(TextStyle.Dim),
				Ink.Accent or Ink.Error => Style.None.WithTextStyle(TextStyle.Bold),
				_ => Style.None
			};

		var color = ink switch
		{
			Ink.Muted => theme.Muted,
			Ink.Accent => theme.Accent ?? theme.Primary,
			Ink.Success => theme.Success,
			Ink.Warning => theme.Warning,
			Ink.Error => theme.Error,
			Ink.Info => theme.Primary,
			Ink.Bright => Colors.TerminalBrightYellow,
			_ => theme.Foreground
		};
		return Style.None.WithForeground(color ?? theme.Foreground ?? Colors.TerminalWhite);
	}
}
