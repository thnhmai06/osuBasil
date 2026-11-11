using System.Diagnostics.Metrics;
using Basil.Application.Diagnostics.Services;

namespace Basil.Host.Rest;

/// <summary>The meter of the API host, which also measures the requests of the other web hosts.</summary>
internal static class RestMeter
{
	/// <summary>Gets the meter.</summary>
	internal static readonly Meter Instance = new("Basil.Rest", "1.0");

	/// <summary>
	///     Request duration in milliseconds, tagged with the host label (<c>c</c>, <c>osu</c>, <c>api</c>...) and status
	///     class.
	/// </summary>
	internal static readonly Histogram<double> HttpDuration =
		Instance.CreateHistogram<double>(DiagnosticsMetrics.HttpDurationName, "ms", "HTTP request duration by host.");
}