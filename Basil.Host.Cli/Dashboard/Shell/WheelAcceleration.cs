namespace Basil.Host.Cli.Dashboard.Shell;

internal sealed class WheelAcceleration(TimeProvider clock, int baseStep = 1)
{
	private const int MaxStreak = 12;
	private static readonly TimeSpan Burst = TimeSpan.FromMilliseconds(120);
	private bool _any;
	private long _last;
	private int _streak;

	public int Step()
	{
		var now = clock.GetTimestamp();
		_streak = _any && clock.GetElapsedTime(_last, now) < Burst ? Math.Min(_streak + 1, MaxStreak) : 0;
		_last = now;
		_any = true;
		return baseStep * (1 + _streak / 3);
	}
}