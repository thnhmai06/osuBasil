using System.Diagnostics.Metrics;

namespace Basil.Application.Diagnostics.Services;

public static class DiagnosticsMetrics
{
	public const string HttpDurationName = "basil.http.request.duration";

	public const string HostTag = "host";

	public const string StatusTag = "status";

	public const string StableDurationName = "basil.stable.poll.duration";

	public const string IrcDurationName = "basil.irc.line.duration";

	/// <summary>The gateway's counter of refused requests; the gateway names it the same way.</summary>
	public const string AdmissionDeniedName = "basil.admission.denied";

	private static readonly Meter Instance = new("Basil.Protocols", "1.0");

	public static readonly Histogram<double> StableDuration =
		Instance.CreateHistogram<double>(StableDurationName, "ms", "Time the game server takes to handle one poll.");

	public static readonly Histogram<double> IrcDuration =
		Instance.CreateHistogram<double>(IrcDurationName, "ms", "Time the IRC server takes to handle one line.");
}