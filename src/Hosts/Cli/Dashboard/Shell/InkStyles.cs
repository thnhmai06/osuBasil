using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Shell;

internal static class InkStyles
{
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
