namespace Basil.Server.Features.Diagnostics;

/// <summary>The web host's live request and connection counters, plus the current request-duration distribution.</summary>
/// <param name="ActiveRequests">The number of HTTP requests currently being handled.</param>
/// <param name="RequestsCompleted">The cumulative number of HTTP requests completed since the process started.</param>
/// <param name="RequestsFailed">The cumulative number of HTTP requests that completed with an error since the process started.</param>
/// <param name="ActiveConnections">The number of Kestrel connections currently open.</param>
/// <param name="ConnectionsCompleted">The cumulative number of Kestrel connections closed since the process started.</param>
/// <param name="RequestDuration">
///     The distribution of how long a completed request took, accumulated over a rolling window: the
///     window resets every time the live stream reads it, so a client watching
///     <c>GET /diagnostic/http/live</c> sees one interval's worth of durations per event rather than a
///     total that only ever grows. A plain <c>GET /diagnostic/http</c> instead looks at whatever has
///     built up in the current window without resetting it, so it never disturbs the live stream's
///     own interval.
/// </param>
public sealed record HttpSample(
	long ActiveRequests,
	long RequestsCompleted,
	long RequestsFailed,
	long ActiveConnections,
	long ConnectionsCompleted,
	DurationAggregateSnapshot RequestDuration);

/// <summary>Samples the web host's live request/connection counters and request-duration distribution.</summary>
/// <remarks>
///     <see cref="Sample" /> and <see cref="SampleAndResetDuration" /> both read the same underlying
///     counters and differ only in how they treat <see cref="RuntimeMeterListener.PeekRequestDuration" />
///     versus <see cref="RuntimeMeterListener.SnapshotRequestDuration" />: the duration distribution
///     is a shared, resettable accumulator, so exactly one caller may be allowed to reset it on a
///     steady cadence, or two independent readers would each think they own the window and silently
///     steal each other's data. <see cref="SampleAndResetDuration" /> is reserved for that one caller
///     (the live broadcast loop); every other reader, including a plain <c>GET</c>, must use
///     <see cref="Sample" />.
/// </remarks>
public sealed class HttpSampler(RuntimeMeterListener meterListener)
{
	/// <summary>Takes a sample without disturbing the request-duration window a live stream may be rotating.</summary>
	public HttpSample Sample() => Build(meterListener.PeekRequestDuration());

	/// <summary>Takes a sample and resets the request-duration window for the next interval.</summary>
	internal HttpSample SampleAndResetDuration() => Build(meterListener.SnapshotRequestDuration());

	private HttpSample Build(DurationAggregateSnapshot requestDuration) => new(
		meterListener.ActiveRequests,
		meterListener.RequestsCompleted,
		meterListener.RequestsFailed,
		meterListener.ActiveConnections,
		meterListener.ConnectionsCompleted,
		requestDuration);
}
