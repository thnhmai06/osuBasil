using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Scrolling;

namespace Basil.Host.Cli.Dashboard.Shell;

internal static class WheelScroll
{
	public static T Accelerate<T>(T host, Func<ScrollViewer?> viewer, TimeProvider clock, Action<bool>? scrolled = null)
		where T : Visual
	{
		var wheel = new WheelAcceleration(clock);
		// Preview runs before the viewer's own handler, which would move one row and mark the event handled.
		host.PointerWheelRouted += (_, e) =>
		{
			if (e.RoutingPhase != RoutingPhase.Preview || e.WheelDelta == 0 || e.Handled ||
			    (e.Modifiers & TerminalModifiers.Shift) != 0 || viewer() is not { } target ||
			    IsOverOther(e.OriginalSource, host, target))
				return;

			var max = Max(target);
			if (max == 0) return;

			e.Handled = true;
			var step = wheel.Step();
			var next = Math.Clamp(target.VerticalOffset + (e.WheelDelta > 0 ? -step : step), 0, max);
			target.VerticalOffset = next;
			scrolled?.Invoke(next >= max);
		};
		return host;
	}

	public static ScrollViewer Accelerate(ScrollViewer viewer, TimeProvider clock)
	{
		return Accelerate(viewer, () => viewer, clock);
	}

	public static LogControl Accelerate(LogControl log, TimeProvider clock)
	{
		return Accelerate(log, () => log.EnumerateVisualsDepthFirst().OfType<ScrollViewer>().FirstOrDefault(), clock,
			atEnd =>
			{
				// Scrolling up already stops the following; only the way back needs help.
				if (atEnd && !log.HasSelection) log.FollowTail = true;
			});
	}

	public static bool AtEnd(ScrollViewer viewer)
	{
		return viewer.VerticalOffset >= Max(viewer);
	}

	private static int Max(ScrollViewer viewer)
	{
		var model = viewer.ContentScrollable?.Scroll;
		var extent = model?.ExtentHeight ?? viewer.Content?.Bounds.Height ?? 0;
		return Math.Max(extent - (model?.ViewportHeight ?? viewer.ViewportHeight), 0);
	}

	// A list or another viewer inside the host takes the wheel for itself.
	private static bool IsOverOther(Visual? origin, Visual host, ScrollViewer target)
	{
		for (var visual = origin; visual is not null && !ReferenceEquals(visual, host); visual = visual.Parent)
			if (visual is LogControl or IScrollable || visual is ScrollViewer other && !ReferenceEquals(other, target))
				return true;

		return false;
	}
}
