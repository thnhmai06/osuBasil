using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Scrolling;

namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Makes the mouse wheel scroll a viewer faster the quicker the notches follow each other.</summary>
internal static class WheelScroll
{
	/// <summary>Makes the wheel over a visual scroll a viewer with <see cref="WheelAcceleration" />.</summary>
	/// <param name="host">The visual the wheel turns over; a viewer or the control that contains one.</param>
	/// <param name="viewer">Gets the viewer to scroll, or <see langword="null" /> when there is none yet.</param>
	/// <param name="clock">Measures the time between notches.</param>
	/// <param name="scrolled">Told after each notch whether the viewer now sits at its end.</param>
	/// <returns>The same host.</returns>
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

	/// <summary>Makes the wheel over a scrolling page scroll it faster.</summary>
	/// <param name="viewer">The viewer.</param>
	/// <param name="clock">Measures the time between notches.</param>
	/// <returns>The same viewer.</returns>
	public static ScrollViewer Accelerate(ScrollViewer viewer, TimeProvider clock)
	{
		return Accelerate(viewer, () => viewer, clock);
	}

	/// <summary>Makes the wheel over a log scroll it faster, and makes the log follow its tail again when it reaches the end.</summary>
	/// <param name="log">The log.</param>
	/// <param name="clock">Measures the time between notches.</param>
	/// <returns>The same log.</returns>
	public static LogControl Accelerate(LogControl log, TimeProvider clock)
	{
		return Accelerate(log, () => log.EnumerateVisualsDepthFirst().OfType<ScrollViewer>().FirstOrDefault(), clock,
			atEnd =>
			{
				// Scrolling up already stops the following; only the way back needs help.
				if (atEnd && !log.HasSelection) log.FollowTail = true;
			});
	}

	/// <summary>Tells whether a viewer shows the end of its content, which is also the case when the content fits.</summary>
	/// <param name="viewer">The viewer.</param>
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
