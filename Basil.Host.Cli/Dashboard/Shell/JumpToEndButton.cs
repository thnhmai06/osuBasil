using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Basil.Host.Cli.Dashboard.Shell;

internal static class JumpToEndButton
{
	public static Visual Over(LogControl log, Glyphs glyphs, Action jump)
	{
		var button = new Button($"[{glyphs.Down} Jump to End]").Click(jump).IsVisible(() => !log.FollowTail)
			.HorizontalAlignment(Align.End).VerticalAlignment(Align.End);
		button.IsTabStop = false;
		return new ZStack(log, button);
	}

	public static void Sync(LogControl log)
	{
		if (log.FollowTail || log.HasSelection || log.Bounds.Height <= 0) return;

		if (log.EnumerateVisualsDepthFirst().OfType<ScrollViewer>().FirstOrDefault() is { } viewer &&
		    WheelScroll.AtEnd(viewer))
			log.FollowTail = true;
	}
}