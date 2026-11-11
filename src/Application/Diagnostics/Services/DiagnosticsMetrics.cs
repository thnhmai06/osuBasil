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
}