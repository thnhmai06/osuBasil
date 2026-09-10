using System.Diagnostics.Metrics;

namespace Basil.Server.Features.Diagnostics;

/// <summary>An immutable snapshot of a request- or connection-duration distribution.</summary>
/// <param name="Count">The number of durations folded into this snapshot.</param>
/// <param name="SumSeconds">The sum of every duration, in seconds.</param>
/// <param name="MinSeconds">The shortest duration observed, or zero when <paramref name="Count" /> is zero.</param>
/// <param name="MaxSeconds">The longest duration observed.</param>
/// <param name="BucketUpperBoundsSeconds">The upper bound, in seconds, of each entry in <paramref name="BucketCounts" />, with the last entry covering everything above the second-to-last bound.</param>
/// <param name="BucketCounts">How many durations fell at or under each of <paramref name="BucketUpperBoundsSeconds" />, in order.</param>
public sealed record DurationAggregateSnapshot(
	long Count,
	double SumSeconds,
	double MinSeconds,
	double MaxSeconds,
	IReadOnlyList<double> BucketUpperBoundsSeconds,
	IReadOnlyList<long> BucketCounts);

/// <summary>
///     Aggregates a stream of duration measurements, in seconds, into a fixed-size histogram that
///     can be read and reset once per broadcast interval.
/// </summary>
/// <remarks>
///     The bucket boundaries are an internal detail of how the distribution is summarized, not a
///     published contract, and carry no per-tag state: every measurement folds into the same fixed
///     bucket set regardless of what tags it was recorded with.
/// </remarks>
internal sealed class DurationAggregate
{
	private static readonly double[] BucketUpperBoundsSeconds =
		[0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

	private readonly Lock _lock = new();
	private readonly long[] _bucketCounts = new long[BucketUpperBoundsSeconds.Length + 1];
	private long _count;
	private double _sumSeconds;
	private double _minSeconds = double.PositiveInfinity;
	private double _maxSeconds;

	/// <summary>Folds one duration, in seconds, into the histogram.</summary>
	public void Record(double seconds)
	{
		var bucket = 0;
		while (bucket < BucketUpperBoundsSeconds.Length && seconds > BucketUpperBoundsSeconds[bucket])
			bucket++;

		lock (_lock)
		{
			_count++;
			_sumSeconds += seconds;
			if (seconds < _minSeconds) _minSeconds = seconds;
			if (seconds > _maxSeconds) _maxSeconds = seconds;
			_bucketCounts[bucket]++;
		}
	}

	/// <summary>Returns the distribution accumulated since the last call, then clears it for the next interval.</summary>
	public DurationAggregateSnapshot SnapshotAndReset()
	{
		lock (_lock)
		{
			var snapshot = BuildSnapshot();

			_count = 0;
			_sumSeconds = 0;
			_minSeconds = double.PositiveInfinity;
			_maxSeconds = 0;
			Array.Clear(_bucketCounts);

			return snapshot;
		}
	}

	/// <summary>
	///     Returns the distribution accumulated so far without resetting it, for a reader that must
	///     not disturb the interval the resetting <see cref="SnapshotAndReset" /> caller owns.
	/// </summary>
	public DurationAggregateSnapshot Peek()
	{
		lock (_lock) return BuildSnapshot();
	}

	private DurationAggregateSnapshot BuildSnapshot() => new(
		_count, _sumSeconds, _count == 0 ? 0 : _minSeconds, _maxSeconds,
		BucketUpperBoundsSeconds, [.. _bucketCounts]);
}

/// <summary>
///     Accumulates the runtime, ASP.NET Core and Basil-published counters that only exist while a
///     listener has been attached, so the diagnostic snapshots can read them back as plain fields.
/// </summary>
/// <remarks>
///     The exception count, the ASP.NET Core hosting and Kestrel counters, and Basil's own SSE
///     counters are all push-based: nothing anywhere exposes a property to read them from cold, and
///     the hosting/Kestrel counters do not exist at all until the web host constructs them. A
///     listener created on demand would therefore have no baseline and could report nothing for the
///     interval an operator cares about, so exactly one listener runs for the process's whole
///     lifetime, owned by the host rather than by whoever happens to be reading it, and every
///     consumer reads its accumulated fields instead of standing up a listener of its own.
///
///     Every field this type exposes is a fixed scalar or a fixed-size histogram. Nothing is keyed by
///     a tag value: a tag such as a thrown exception's type name can take arbitrarily many distinct
///     values over a server's lifetime, and keying an accumulator by it would make this component's
///     own memory grow with the traffic it exists to watch. The number of independent series this
///     type tracks is therefore fixed at compile time and never grows, no matter how many distinct
///     tag values pass through it -- <see cref="ActiveSseSubscribers" /> and
///     <see cref="SsePublishesDropped" /> fold every <c>stream</c> tag value into the same two
///     totals rather than breaking the count out per stream, for the same reason.
/// </remarks>
public sealed class RuntimeMeterListener : IHostedService, IDisposable
{
	private readonly MeterListener _listener = new();
	private readonly DurationAggregate _requestDuration = new();

	private long _exceptionsThrown;
	private long _activeRequests;
	private long _requestsCompleted;
	private long _requestsFailed;
	private long _activeConnections;
	private long _connectionsCompleted;
	private long _activeSseSubscribers;
	private long _ssePublishesDropped;
	private long _activeMatches;
	private long _activeMatchTimers;
	private long _activeChannels;
	private long _activeIrcSessions;

	public RuntimeMeterListener()
	{
		_listener.InstrumentPublished = OnInstrumentPublished;
		_listener.SetMeasurementEventCallback<int>(OnIntMeasurement);
		_listener.SetMeasurementEventCallback<long>(OnLongMeasurement);
		_listener.SetMeasurementEventCallback<double>(OnDoubleMeasurement);
	}

	/// <summary>The cumulative count of exceptions thrown anywhere in the process since the listener started.</summary>
	public long ExceptionsThrown => Interlocked.Read(ref _exceptionsThrown);

	/// <summary>The number of HTTP requests currently being handled.</summary>
	public long ActiveRequests => Interlocked.Read(ref _activeRequests);

	/// <summary>The cumulative count of HTTP requests completed since the listener started.</summary>
	public long RequestsCompleted => Interlocked.Read(ref _requestsCompleted);

	/// <summary>The cumulative count of HTTP requests that completed with an error since the listener started.</summary>
	public long RequestsFailed => Interlocked.Read(ref _requestsFailed);

	/// <summary>The number of Kestrel connections currently open.</summary>
	public long ActiveConnections => Interlocked.Read(ref _activeConnections);

	/// <summary>The cumulative count of Kestrel connections closed since the listener started.</summary>
	public long ConnectionsCompleted => Interlocked.Read(ref _connectionsCompleted);

	/// <summary>The number of Server-Sent Events connections currently open, across every stream Basil publishes.</summary>
	public long ActiveSseSubscribers => Interlocked.Read(ref _activeSseSubscribers);

	/// <summary>
	///     The cumulative count of stream publishes dropped since the listener started for arriving
	///     out of order relative to a newer one already applied to the same stream -- a benign,
	///     by-design race outcome under concurrent load, not an error count.
	/// </summary>
	public long SsePublishesDropped => Interlocked.Read(ref _ssePublishesDropped);

	/// <summary>The number of multiplayer matches currently registered, as of the last <see cref="RefreshObservableGauges" /> call.</summary>
	public long ActiveMatches => Interlocked.Read(ref _activeMatches);

	/// <summary>The number of multiplayer matches with a countdown currently running, as of the last <see cref="RefreshObservableGauges" /> call.</summary>
	public long ActiveMatchTimers => Interlocked.Read(ref _activeMatchTimers);

	/// <summary>The number of chat channels currently registered, as of the last <see cref="RefreshObservableGauges" /> call.</summary>
	public long ActiveChannels => Interlocked.Read(ref _activeChannels);

	/// <summary>The number of IRC sessions currently online, as of the last <see cref="RefreshObservableGauges" /> call.</summary>
	public long ActiveIrcSessions => Interlocked.Read(ref _activeIrcSessions);

	/// <summary>Whether the listener has been stopped and its underlying resources released.</summary>
	internal bool Disposed { get; private set; }

	/// <summary>Returns the request-duration distribution accumulated since the last call, then clears it.</summary>
	public DurationAggregateSnapshot SnapshotRequestDuration() => _requestDuration.SnapshotAndReset();

	/// <summary>
	///     Returns the request-duration distribution accumulated so far without resetting it, for a
	///     reader that only wants to look at the current window, not own its rotation.
	/// </summary>
	public DurationAggregateSnapshot PeekRequestDuration() => _requestDuration.Peek();

	/// <summary>
	///     Polls every observable gauge this listener is tracking -- <see cref="ActiveMatches" />,
	///     <see cref="ActiveMatchTimers" />, <see cref="ActiveChannels" /> and
	///     <see cref="ActiveIrcSessions" /> -- and updates them with the value each gauge's owning
	///     slice reports right now. Unlike the push-based counters this listener also tracks, an
	///     observable gauge never calls back on its own; nothing updates until this is called.
	/// </summary>
	public void RefreshObservableGauges() => _listener.RecordObservableInstruments();

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		_listener.Start();
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (Disposed) return;
		Disposed = true;
		_listener.Dispose();
	}

	/// <summary>
	///     Feeds one int-valued measurement through the same routing the live listener uses, for
	///     tests. Named distinctly from the other <c>RecordForTest</c> overloads rather than
	///     overloaded on <see langword="int" />: an int literal at an existing <c>long</c>-typed call
	///     site would silently rebind to this overload instead of raising an ambiguity, changing
	///     which accumulator the call updates without a compile error.
	/// </summary>
	internal void RecordIntForTest(string instrumentName, int measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
		RecordInt(instrumentName, measurement, tags);

	/// <summary>Feeds one long-valued measurement through the same routing the live listener uses, for tests.</summary>
	internal void RecordForTest(string instrumentName, long measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
		RecordLong(instrumentName, measurement, tags);

	/// <summary>Feeds one double-valued measurement through the same routing the live listener uses, for tests.</summary>
	internal void RecordForTest(string instrumentName, double measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
		RecordDouble(instrumentName, measurement, tags);

	private static bool IsTracked(Instrument instrument) => instrument.Meter.Name switch
	{
		"System.Runtime" => instrument.Name is "dotnet.exceptions",
		"Microsoft.AspNetCore.Hosting" => instrument.Name is
			"http.server.active_requests" or "http.server.request.duration",
		"Microsoft.AspNetCore.Server.Kestrel" => instrument.Name is
			"kestrel.active_connections" or "kestrel.connection.duration",
		"Basil" => instrument.Name is "basil.sse.subscribers" or "basil.match.publish.stale_dropped"
			or "basil.matches.active" or "basil.match.timers.active" or "basil.channels.active"
			or "basil.irc.sessions.active",
		_ => false
	};

	private void OnInstrumentPublished(Instrument instrument, MeterListener listener)
	{
		if (IsTracked(instrument)) listener.EnableMeasurementEvents(instrument);
	}

	private void OnIntMeasurement(Instrument instrument, int measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
		RecordInt(instrument.Name, measurement, tags);

	private void OnLongMeasurement(Instrument instrument, long measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
		RecordLong(instrument.Name, measurement, tags);

	private void OnDoubleMeasurement(Instrument instrument, double measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
		RecordDouble(instrument.Name, measurement, tags);

	private void RecordInt(string instrumentName, int measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags)
	{
		// basil.sse.subscribers is push-based (an UpDownCounter each subscribe/unsubscribe reports as
		// a delta), so it accumulates. The four gauges below are pull-based: each measurement is the
		// owning slice's current count as of this poll, not a delta, so it replaces rather than adds.
		switch (instrumentName)
		{
			case "basil.sse.subscribers":
				Interlocked.Add(ref _activeSseSubscribers, measurement);
				break;
			case "basil.matches.active":
				Interlocked.Exchange(ref _activeMatches, measurement);
				break;
			case "basil.match.timers.active":
				Interlocked.Exchange(ref _activeMatchTimers, measurement);
				break;
			case "basil.channels.active":
				Interlocked.Exchange(ref _activeChannels, measurement);
				break;
			case "basil.irc.sessions.active":
				Interlocked.Exchange(ref _activeIrcSessions, measurement);
				break;
		}
	}

	private void RecordLong(string instrumentName, long measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags)
	{
		switch (instrumentName)
		{
			case "dotnet.exceptions":
				Interlocked.Add(ref _exceptionsThrown, measurement);
				break;
			case "http.server.active_requests":
				Interlocked.Add(ref _activeRequests, measurement);
				break;
			case "kestrel.active_connections":
				Interlocked.Add(ref _activeConnections, measurement);
				break;
			case "basil.match.publish.stale_dropped":
				Interlocked.Add(ref _ssePublishesDropped, measurement);
				break;
		}
	}

	private void RecordDouble(string instrumentName, double measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags)
	{
		switch (instrumentName)
		{
			case "http.server.request.duration":
				Interlocked.Increment(ref _requestsCompleted);
				if (HasErrorTag(tags)) Interlocked.Increment(ref _requestsFailed);
				_requestDuration.Record(measurement);
				break;
			case "kestrel.connection.duration":
				Interlocked.Increment(ref _connectionsCompleted);
				break;
		}
	}

	private static bool HasErrorTag(ReadOnlySpan<KeyValuePair<string, object?>> tags)
	{
		foreach (var tag in tags)
			if (tag.Key == "error.type")
				return true;
		return false;
	}
}
