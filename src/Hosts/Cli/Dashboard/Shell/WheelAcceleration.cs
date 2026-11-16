namespace Basil.Host.Cli.Dashboard.Shell;

/// <summary>Turns the notches of a mouse wheel into a step that grows while the notches follow each other quickly.</summary>
/// <param name="clock">Measures the time between notches.</param>
/// <param name="baseStep">How many rows a lone notch moves.</param>
internal sealed class WheelAcceleration(TimeProvider clock, int baseStep = 1)
{
	/// <summary>Notches closer than this belong to one burst.</summary>
	private static readonly TimeSpan Burst = TimeSpan.FromMilliseconds(120);

	private const int MaxStreak = 12;
	private long _last;
	private bool _any;
	private int _streak;

	/// <summary>Counts a notch of the wheel now.</summary>
	/// <returns>How many rows the notch moves: the base step, up to five times that in a long burst.</returns>
	public int Step()
	{
		var now = clock.GetTimestamp();
		_streak = _any && clock.GetElapsedTime(_last, now) < Burst ? Math.Min(_streak + 1, MaxStreak) : 0;
		_last = now;
		_any = true;
		return baseStep * (1 + _streak / 3);
	}
}
