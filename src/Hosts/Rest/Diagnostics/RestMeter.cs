using System.Diagnostics.Metrics;
using Basil.Application.Diagnostics.Services;

namespace Basil.Host.Rest.Diagnostics;

internal static class RestMeter
{
	internal static readonly Meter Instance = new("Basil.Rest", "1.0");

	internal static readonly Histogram<double> HttpDuration =
		Instance.CreateHistogram<double>(DiagnosticsMetrics.HttpDurationName, "ms", "HTTP request duration by host.");
}