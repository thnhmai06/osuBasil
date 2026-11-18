using System.Diagnostics;

namespace Basil.Testing.Persistence;

public static class Eventually
{
	private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

	public static async Task TrueAsync(Func<bool> condition, string because)
	{
		var started = Stopwatch.GetTimestamp();
		while (!condition())
		{
			if (Stopwatch.GetElapsedTime(started) > Deadline)
				throw new TimeoutException($"Expected: {because}.");
			await Task.Delay(Interval, TestContext.Current.CancellationToken);
		}
	}
}