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
			var snapshot = new DurationAggregateSnapshot(
				_count, _sumSeconds, _count == 0 ? 0 : _minSeconds, _maxSeconds,
				BucketUpperBoundsSeconds, [.. _bucketCounts]);

			_count = 0;
			_sumSeconds = 0;
			_minSeconds = double.PositiveInfinity;
			_maxSeconds = 0;
			Array.Clear(_bucketCounts);

			return snapshot;
		}
	}
}

/// <summary>
///     Accumulates the runtime and ASP.NET Core counters that only exist while a listener has been
///     attached, so the diagnostic snapshots can read them back as plain fields.
/// </summary>
/// <remarks>
///     The exception count and the ASP.NET Core hosting and Kestrel counters are push-based: nothing
///     anywhere exposes a property to read them from cold, and the hosting/Kestrel counters do not
///     exist at all until the web host constructs them. A listener created on demand would therefore
///     have no baseline and could report nothing for the interval an operator cares about, so exactly
///     one listener runs for the process's whole lifetime, owned by the host rather than by whoever
///     happens to be reading it, and every consumer reads its accumulated fields instead of standing
///     up a listener of its own.
///
///     Every field this type exposes is a fixed scalar or a fixed-size histogram. Nothing is keyed by
///     a tag value: a tag such as a thrown exception's type name can take arbitrarily many distinct
///     values over a server's lifetime, and keying an accumulator by it would make this component's
///     own memory grow with the traffic it exists to watch. The number of independent series this
///     type tracks is therefore fixed at compile time and never grows, no matter how many distinct
///     tag values pass through it.
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

	public RuntimeMeterListener()
	{
		_listener.InstrumentPublished = OnInstrumentPublished;
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

	/// <summary>Whether the listener has been stopped and its underlying resources released.</summary>
	internal bool Disposed { get; private set; }

	/// <summary>Returns the request-duration distribution accumulated since the last call, then clears it.</summary>
	public DurationAggregateSnapshot SnapshotRequestDuration() => _requestDuration.SnapshotAndReset();

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
		_ => false
	};

	private void OnInstrumentPublished(Instrument instrument, MeterListener listener)
	{
		if (IsTracked(instrument)) listener.EnableMeasurementEvents(instrument);
	}

	private void OnLongMeasurement(Instrument instrument, long measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
		RecordLong(instrument.Name, measurement, tags);

	private void OnDoubleMeasurement(Instrument instrument, double measurement,
		ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state) =>
		RecordDouble(instrument.Name, measurement, tags);

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
