using System.Diagnostics.Metrics;

namespace Basil.Host.Shared.Diagnostics;

/// <summary>The one meter every host publishes its measurements on; the diagnostics and the dashboard read it.</summary>
public static class BasilMeter
{
	/// <summary>Gets the meter.</summary>
	public static readonly Meter Instance = new("Basil", "1.0");

	/// <summary>Request duration in milliseconds, tagged with the host label (<c>c</c>, <c>osu</c>, <c>api</c>...) and status class.</summary>
	public static readonly Histogram<double> HttpDuration =
		Instance.CreateHistogram<double>("basil.http.request.duration", "ms", "HTTP request duration by host.");
}
