namespace Basil.LoadTests.Hosting;

/// <summary>
///     What a given <see cref="IServerHost" /> can actually observe about the server it fronts. Printed
///     into <c>run.json</c> so a report can never silently imply a metric that was not really measured —
///     e.g. a Docker run's GC-heap column must read "not available on this host", never "0".
/// </summary>
/// <param name="CanMeasureStartupTime">Whether this host started the process itself and can time it.</param>
/// <param name="CanMeasureProcessMetrics">Whether CPU/working-set/private-memory/threads/handles are observable.</param>
/// <param name="CanMeasureDotnetCounters">Whether GC-heap/gen-count/alloc-rate/threadpool counters are observable.</param>
/// <param name="CanSnapshotDatabase">Whether this host may snapshot/restore the server's database between runs.</param>
public sealed record ServerHostCapabilities(
	bool CanMeasureStartupTime,
	bool CanMeasureProcessMetrics,
	bool CanMeasureDotnetCounters,
	bool CanSnapshotDatabase)
{
	/// <summary>The capability set for a host with no observability at all (a bare <see cref="ExistingServerHost" />).</summary>
	public static readonly ServerHostCapabilities None = new(false, false, false, false);
}
