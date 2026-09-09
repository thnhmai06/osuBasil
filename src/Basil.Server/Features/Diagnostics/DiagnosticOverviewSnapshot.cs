namespace Basil.Server.Features.Diagnostics;

/// <summary>A curated, at-a-glance subset of the full diagnostic categories, for one dashboard screen.</summary>
/// <param name="CpuUsagePercent">The process's CPU usage, as a share of all available cores, since the previous sample.</param>
/// <param name="WorkingSetBytes">The process's current physical memory footprint.</param>
/// <param name="TimeInGcPercent">The share of wall-clock time spent paused for garbage collection since the previous sample.</param>
/// <param name="ActiveHttpRequests">The number of HTTP requests currently being handled.</param>
/// <param name="ExceptionsThrown">The cumulative number of exceptions thrown since the process started listening for them.</param>
/// <param name="ActiveGameSessions">The number of osu! client sessions currently logged in.</param>
/// <param name="ActiveMatches">The number of multiplayer matches currently registered.</param>
/// <param name="ActiveSseSubscribers">The number of Server-Sent Events connections currently open, across every stream Basil publishes.</param>
public sealed record DiagnosticOverviewSample(
	double CpuUsagePercent,
	long WorkingSetBytes,
	double TimeInGcPercent,
	long ActiveHttpRequests,
	long ExceptionsThrown,
	int ActiveGameSessions,
	long ActiveMatches,
	long ActiveSseSubscribers);

/// <summary>
///     Samples the fields <c>GET /diagnostic/live</c> reports: the smallest set an operator watching
///     one screen during a tournament needs to answer "is the box under load, and is the tournament
///     actually running" without switching between every category's own stream.
/// </summary>
/// <remarks>
///     Deliberately not the union of every category (every other category's own <c>/live</c> stream
///     already covers that in full). Chosen here:
///     <list type="bullet">
///         <item>CPU and working set answer "is the box under load."</item>
///         <item>Time-in-GC answers "is memory pressure the cause, if it is."</item>
///         <item>Active HTTP requests and exceptions-thrown answer "is the API serving traffic without errors."</item>
///         <item>Active game sessions and matches answer "is the tournament actually running right now."</item>
///         <item>Active SSE subscribers answers "are the stream consumers (overlays, dashboards) still connected."</item>
///     </list>
///     Thread pool, Kestrel connection counts, GC generation sizes and the rest are real signals, just
///     not ones an operator needs on the first screen; <c>GET /diagnostic/{category}</c> and its
///     <c>/live</c> sibling cover them.
///
///     Uses <see cref="HttpSampler.Sample" />, not <see cref="HttpSampler.SampleAndResetDuration" />:
///     the overview never reads the request-duration distribution at all, so it has no reason to
///     touch the window the <c>http</c> category's own live stream rotates.
/// </remarks>
public sealed class DiagnosticOverviewSampler(
	ProcessSampler process,
	GcSampler gc,
	HttpSampler http,
	ExceptionsSampler exceptions,
	ApplicationSampler application)
{
	/// <summary>Takes a fresh curated sample.</summary>
	public DiagnosticOverviewSample Sample()
	{
		var processSample = process.Sample();
		var gcSample = gc.Sample();
		var httpSample = http.Sample();
		var exceptionsSample = exceptions.Sample();
		var applicationSample = application.Sample();

		return new DiagnosticOverviewSample(
			processSample.CpuUsagePercent,
			processSample.WorkingSetBytes,
			gcSample.TimeInGcPercent,
			httpSample.ActiveRequests,
			exceptionsSample.TotalThrown,
			applicationSample.ActiveGameSessions,
			applicationSample.ActiveMatches,
			applicationSample.ActiveSseSubscribers);
	}
}
