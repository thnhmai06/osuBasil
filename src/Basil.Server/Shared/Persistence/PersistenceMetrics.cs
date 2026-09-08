using System.Diagnostics.Metrics;

namespace Basil.Server.Shared.Persistence;

/// <summary>SQLite instruments published on <see cref="BasilMeter" />.</summary>
public static class PersistenceMetrics
{
	/// <summary>SQLite write-operation duration in milliseconds, tagged <c>operation</c>.</summary>
	public static readonly Histogram<double> DbCommandDurationMs =
		BasilMeter.Instance.CreateHistogram<double>("basil.db.command.duration", "ms",
			"SQLite write-operation duration for the paths under investigation in ADR-001.");

	/// <summary>Count of <c>SQLITE_BUSY</c> occurrences, tagged <c>operation</c>.</summary>
	public static readonly Counter<long> DbBusyCount =
		BasilMeter.Instance.CreateCounter<long>("basil.db.busy", null,
			"SQLITE_BUSY occurrences on the write paths under instrumentation.");
}
