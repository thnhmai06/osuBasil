using System.Diagnostics.Metrics;

namespace Basil.Server.Shared.Eventing;

/// <summary>SSE eventing instruments published on <see cref="BasilMeter" />.</summary>
public static class EventingMetrics
{
	/// <summary>Currently connected SSE subscribers, tagged <c>stream</c>.</summary>
	public static readonly UpDownCounter<int> SseActiveSubscribers =
		BasilMeter.Instance.CreateUpDownCounter<int>("basil.sse.subscribers", null,
			"Currently connected SSE subscribers by stream kind.");

	/// <summary>Bounded-channel backlog depth observed at publish time, tagged <c>stream</c>.</summary>
	public static readonly Histogram<int> SseBacklogDepth =
		BasilMeter.Instance.CreateHistogram<int>("basil.sse.backlog_depth", null,
			"Channel backlog depth observed when publishing an SSE event.");

	/// <summary>
	///     Count of publishes dropped by a <see cref="SequenceGate" /> for being superseded
	///     by a newer one, tagged <c>stream</c>.
	/// </summary>
	/// <remarks>
	///     Expected to be nonzero under real concurrent load (ADR-004 4b) — this is a benign,
	///     by-design race outcome, not an error condition.
	/// </remarks>
	public static readonly Counter<long> StalePublishDropped =
		BasilMeter.Instance.CreateCounter<long>("basil.match.publish.stale_dropped", null,
			"Publishes dropped for arriving out of order relative to a newer one already applied.");
}
