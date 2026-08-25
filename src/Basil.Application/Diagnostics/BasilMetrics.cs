using System.Diagnostics.Metrics;

namespace Basil.Application.Diagnostics;

/// <summary>
///     The single <see cref="Meter" /> Basil publishes diagnostics through, and the instruments the
///     2026 performance investigation identified as necessary to answer questions the load-test
///     corpus alone could not: is a stall steady contention or a periodic event (DB writes), is a
///     wait actually blocking work or just idle (match locks), and is a subsystem accumulating state
///     it should not (SSE subscribers). This is deliberately not a full OpenTelemetry setup — no
///     exporter is wired here; any host that wants the data attaches a listener
///     (<c>dotnet-counters monitor --process-id &lt;pid&gt; Basil</c> or an OTel collector) to the
///     <see cref="MeterName" /> meter.
/// </summary>
public static class BasilMetrics
{
	/// <summary>The meter name every Basil instrument is published under.</summary>
	public const string MeterName = "Basil";

	private static readonly Meter Meter = new(MeterName);

	/// <summary>Request duration in milliseconds, tagged <c>host.group</c> (bancho/api/assets/...).</summary>
	public static readonly Histogram<double> RequestDurationMs =
		Meter.CreateHistogram<double>("basil.http.request.duration", "ms",
			"HTTP request duration by Basil host group.");

	/// <summary>SQLite write-operation duration in milliseconds, tagged <c>operation</c>.</summary>
	public static readonly Histogram<double> DbCommandDurationMs =
		Meter.CreateHistogram<double>("basil.db.command.duration", "ms",
			"SQLite write-operation duration for the paths under investigation in ADR-001.");

	/// <summary>Count of <c>SQLITE_BUSY</c> occurrences, tagged <c>operation</c>.</summary>
	public static readonly Counter<long> DbBusyCount =
		Meter.CreateCounter<long>("basil.db.busy", null,
			"SQLITE_BUSY occurrences on the write paths under instrumentation.");

	/// <summary>Time spent waiting to acquire a <c>MatchSession.Lock</c>, in milliseconds.</summary>
	public static readonly Histogram<double> MatchLockWaitMs =
		Meter.CreateHistogram<double>("basil.match.lock_wait.duration", "ms",
			"Time a caller spent waiting for a match's lock before acquiring it.");

	/// <summary>Currently connected SSE subscribers, tagged <c>stream</c>.</summary>
	public static readonly UpDownCounter<int> SseActiveSubscribers =
		Meter.CreateUpDownCounter<int>("basil.sse.subscribers", null,
			"Currently connected SSE subscribers by stream kind.");

	/// <summary>Bounded-channel backlog depth observed at publish time, tagged <c>stream</c>.</summary>
	public static readonly Histogram<int> SseBacklogDepth =
		Meter.CreateHistogram<int>("basil.sse.backlog_depth", null,
			"Channel backlog depth observed when publishing an SSE event.");
}
