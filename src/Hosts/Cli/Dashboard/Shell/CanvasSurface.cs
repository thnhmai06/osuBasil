using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Text;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Paints a <see cref="CanvasContext" /> as a dashboard surface, mapping tones to the colours of the theme.</summary>
internal sealed class CanvasSurface(CanvasContext context, Theme theme) : IPaintSurface
{
	private static readonly Color Low = Colors.TerminalGreen;

	/// <inheritdoc />
	public int Width => context.Size.Width;

	/// <inheritdoc />
	public int Height => context.Size.Height;

	/// <inheritdoc />
	public void Put(int x, int y, char character, Tone tone, double heat = 0)
	{
		if (x < 0 || y < 0 || x >= Width || y >= Height) return;

		context.WriteText(x, y, character.ToString(), StyleOf(tone, heat));
	}

	private Style StyleOf(Tone tone, double heat)
	{
		var foreground = theme.Foreground ?? Colors.TerminalWhite;
		return tone switch
		{
			Tone.Muted => Style.None.WithForeground(theme.Muted ?? Colors.TerminalBrightBlack),
			Tone.Accent => Style.None.WithForeground(theme.Accent ?? theme.Primary ?? Colors.TerminalCyan),
			Tone.Good => Style.None.WithForeground(theme.Success ?? Colors.TerminalGreen),
			Tone.Warn => Style.None.WithForeground(theme.Warning ?? Colors.TerminalYellow),
			Tone.Bad => Style.None.WithForeground(theme.Error ?? Colors.TerminalRed),
			Tone.Selected => Style.None.WithForeground(foreground)
				.WithBackground(theme.Selection ?? Colors.TerminalBlue),
			Tone.Heat => Style.None.WithForeground(Gradient(heat)),
			_ => Style.None.WithForeground(foreground)
		};
	}

	private Color Gradient(double heat)
	{
		var good = theme.Success ?? Low;
		var warn = theme.Warning ?? Colors.TerminalYellow;
		var bad = theme.Error ?? Colors.TerminalRed;
		var value = (float)Math.Clamp(heat, 0, 1);
		return value < 0.5f
			? good.Mix(warn, value * 2, ColorMixSpace.Oklab)
			: warn.Mix(bad, (value - 0.5f) * 2, ColorMixSpace.Oklab);
	}
}
