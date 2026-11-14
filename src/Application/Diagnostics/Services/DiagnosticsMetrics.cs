using System.Diagnostics.Metrics;

namespace Basil.Application.Diagnostics.Services;

/// <summary>The names the hosts and the diagnostics agree on for the measurements the hosts take.</summary>
public static class DiagnosticsMetrics
{
	/// <summary>Gets the name of the instrument that records the duration of a request in milliseconds.</summary>
	public const string HttpDurationName = "basil.http.request.duration";

	/// <summary>Gets the tag that carries the host label of a request, one of <see cref="HostLabels.All" />.</summary>
	public const string HostTag = "host";

	/// <summary>Gets the tag that carries the status class of a response, such as <c>2xx</c>.</summary>
	public const string StatusTag = "status";

	/// <summary>Gets the name of the instrument that records how long the game server takes to handle one poll, in milliseconds.</summary>
	public const string StableDurationName = "basil.stable.poll.duration";

	/// <summary>Gets the name of the instrument that records how long the IRC server takes to handle one line, in milliseconds.</summary>
	public const string IrcDurationName = "basil.irc.line.duration";

	private static readonly Meter Instance = new("Basil.Protocols", "1.0");

	/// <summary>Records the time the game server takes to handle one poll of a client, in milliseconds.</summary>
	public static readonly Histogram<double> StableDuration =
		Instance.CreateHistogram<double>(StableDurationName, "ms", "Time the game server takes to handle one poll.");

	/// <summary>Records the time the IRC server takes to handle one line of a client, in milliseconds.</summary>
	public static readonly Histogram<double> IrcDuration =
		Instance.CreateHistogram<double>(IrcDurationName, "ms", "Time the IRC server takes to handle one line.");
}