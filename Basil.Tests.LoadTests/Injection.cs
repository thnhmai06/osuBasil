using NBomber.Contracts;
using NBomber.CSharp;

namespace Basil.Tests.LoadTests;

/// <summary>Builds open-model arrivals that are spread over time instead of arriving all at once.</summary>
public static class Injection
{
	/// <summary>
	/// Injects <paramref name="ratePerMinute"/> copies per minute in even small batches: a rate below 60 arrives as one
	/// copy every <c>60 / rate</c> seconds, a higher rate as the fewest copies per batch that keep the batches at most
	/// one second apart, never the whole minute at one instant.
	/// </summary>
	public static LoadSimulation PerMinute(int ratePerMinute, TimeSpan duration)
	{
		if (ratePerMinute <= 0) return Simulation.Pause(duration);

		var batch = (int)Math.Ceiling(ratePerMinute / 60.0);
		var interval = TimeSpan.FromSeconds(60.0 * batch / ratePerMinute);
		return Simulation.Inject(batch, interval, duration);
	}
}
