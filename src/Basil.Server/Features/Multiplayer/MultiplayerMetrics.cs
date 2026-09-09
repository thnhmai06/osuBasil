using Basil.Server.Shared;
using System.Diagnostics.Metrics;

namespace Basil.Server.Features.Multiplayer;

/// <summary>Match-lock instruments published on <see cref="BasilMeter" />.</summary>
public static class MultiplayerMetrics
{
	/// <summary>Time spent waiting to acquire a <c>MatchSession.Lock</c>, in milliseconds.</summary>
	public static readonly Histogram<double> MatchLockWaitMs =
		BasilMeter.Instance.CreateHistogram<double>("basil.match.lock_wait.duration", "ms",
			"Time a caller spent waiting for a match's lock before acquiring it.");
}

/// <summary>
///     Publishes the live match counts <see cref="IMatchRegistry" /> owns as observable gauges on
///     <see cref="BasilMeter" />, for the process's whole lifetime. Diagnostics reads these back
///     through the meter instead of holding a reference to this slice's own registry.
/// </summary>
/// <remarks>
///     Both callbacks run on the meter's own collection thread and must never take a lock, await, or
///     walk a large structure. <see cref="IMatchRegistry.All" /> is backed by a
///     <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}" />, whose
///     <c>Values.Count</c> is an O(1) field read, not a walk. The countdown gauge does walk the
///     collection once to count matches with <see cref="MatchSession.PendingTimer" /> set, but that
///     walk is bounded by the number of concurrently active matches -- a tournament server's realistic
///     ceiling, not request traffic -- and reads <c>PendingTimer</c> the same lock-free way
///     <see cref="MatchLiveSnapshotBuilder.BuildTimer" /> already does for the plain HTTP timer route.
/// </remarks>
public sealed class MultiplayerMetricsPublisher(IMatchRegistry matches) : IHostedService
{

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		BasilMeter.Instance.CreateObservableGauge("basil.matches.active",
			() => matches.All.Count,
			description: "The number of multiplayer matches currently registered.");
		BasilMeter.Instance.CreateObservableGauge("basil.match.timers.active",
			() => matches.All.Count(m => m.PendingTimer is not null),
			description: "The number of multiplayer matches with a countdown currently running.");
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		// Nothing to release. An instrument's lifetime is its meter's, and the meter here is the
		// process-wide singleton, so the gauge stops being collected when the process does.
		return Task.CompletedTask;
	}
}
