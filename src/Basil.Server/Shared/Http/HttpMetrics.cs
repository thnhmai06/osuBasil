using System.Diagnostics.Metrics;

namespace Basil.Server.Shared.Http;

/// <summary>HTTP request instruments published on <see cref="BasilMeter" />.</summary>
public static class HttpMetrics
{
	/// <summary>Request duration in milliseconds, tagged <c>host.group</c> (bancho/api/assets/...).</summary>
	public static readonly Histogram<double> RequestDurationMs =
		BasilMeter.Instance.CreateHistogram<double>("basil.http.request.duration", "ms",
			"HTTP request duration by Basil host group.");
}
