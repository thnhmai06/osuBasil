using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>The button that takes a log back to its newest line; it floats over the log and shows only when the log is not at its end.</summary>
internal static class JumpToEndButton
{
	/// <summary>Puts the button over the bottom right corner of a log.</summary>
	/// <param name="log">The log that follows its newest line.</param>
	/// <param name="glyphs">The characters to draw with.</param>
	/// <param name="jump">Takes the log to its end.</param>
	public static Visual Over(LogControl log, Glyphs glyphs, Action jump)
	{
		var button = new Button($"[{glyphs.Down} Jump to End]").Click(jump).IsVisible(() => !log.FollowTail)
			.HorizontalAlignment(Align.End).VerticalAlignment(Align.End);
		button.IsTabStop = false;
		return new ZStack(log, button);
	}
}
