using System.Diagnostics;

namespace Basil.Infrastructure.Persistence.Tests.Support;

/// <summary>Waits for something that happens on its own thread, such as a file system event, by polling up to a deadline.</summary>
internal static class Eventually
{
	private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

	/// <summary>Polls a condition until it holds.</summary>
	/// <param name="condition">The condition; it may do something each time it is polled.</param>
	/// <param name="because">What was expected, for the failure message.</param>
	/// <exception cref="TimeoutException">The condition did not hold before the deadline.</exception>
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
