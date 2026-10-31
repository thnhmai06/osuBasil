using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Basil.Host.Shared.Diagnostics;

/// <summary>
///     Measures the process once a second: the runtime categories of the diagnostics that need nothing from the
///     application, shared by the HTTP API and the console dashboard.
/// </summary>
public sealed class RuntimeSampler : IDisposable
{
	private const int RecentExceptions = 20;
	private static readonly string[] Categories = ["process", "gc", "threadpool", "runtime", "exceptions", "http"];
	private readonly TimeProvider _clock;
	private readonly ConcurrentDictionary<string, HostWindow> _hosts = new();
	private readonly MeterListener _listener = new();
	private readonly ConcurrentQueue<string> _recent = new();
	private readonly DateTimeOffset _started;
	private readonly object _sync = new();
	private Dictionary<string, object> _cache = new();
	private long _exceptions;
	private long _lastAllocated;
	private long _lastCompleted;
	private TimeSpan _lastCpu;
	private long _lastExceptions;
	private DateTimeOffset _lastSample;

	/// <summary>Starts counting exceptions and request durations.</summary>
	/// <param name="clock">The clock of the server.</param>
	public RuntimeSampler(TimeProvider clock)
	{
		_clock = clock;
		_started = clock.GetUtcNow();
		_lastSample = _started;
		AppDomain.CurrentDomain.FirstChanceException += OnException;
		// The meter must exist before the listener starts, or its own creation would hide the instrument from the listener.
		var duration = BasilMeter.HttpDuration;
		_listener.InstrumentPublished = (instrument, listener) =>
		{
			if (ReferenceEquals(instrument, duration)) listener.EnableMeasurementEvents(instrument);
		};
		_listener.SetMeasurementEventCallback<double>(OnDuration);
		_listener.Start();
	}

	/// <summary>Gets the names of the categories this sampler fills.</summary>
	public static IReadOnlyList<string> Names => Categories;

	/// <summary>Gets or sets how the sample reads the number of log events dropped because the log queue was full.</summary>
	public Func<long> LogsDropped { get; set; } = () => 0;

	/// <inheritdoc />
	public void Dispose()
	{
		AppDomain.CurrentDomain.FirstChanceException -= OnException;
		_listener.Dispose();
	}

	/// <summary>Takes the sample of the second; sampling again within the same second returns the same values.</summary>
	/// <returns>The value of each category.</returns>
	public IReadOnlyDictionary<string, object> Sample()
	{
		lock (_sync)
		{
			var now = _clock.GetUtcNow();
			var elapsed = (now - _lastSample).TotalSeconds;
			if (elapsed < 0.5 && _cache.Count > 0) return _cache;

			elapsed = Math.Max(elapsed, 1e-3);
			using var process = Process.GetCurrentProcess();
			var cpu = process.TotalProcessorTime;
			var allocated = GC.GetTotalAllocatedBytes();
			var gc = GC.GetGCMemoryInfo();
			var exceptions = Interlocked.Read(ref _exceptions);
			var completed = ThreadPool.CompletedWorkItemCount;
			var result = new Dictionary<string, object>
			{
				["process"] = new
				{
					cpuPercent = Math.Round((cpu - _lastCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100,
						2),
					workingSetBytes = process.WorkingSet64, privateMemoryBytes = process.PrivateMemorySize64,
					handles = process.HandleCount, threads = process.Threads.Count
				},
				["gc"] = new
				{
					heapBytes = gc.HeapSizeBytes,
					generations = gc.GenerationInfo.ToArray().Select(g => g.SizeAfterBytes),
					collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
					pauseMilliseconds = GC.GetTotalPauseDuration().TotalMilliseconds,
					allocatedBytesPerSecond = (long)((allocated - _lastAllocated) / elapsed)
				},
				["threadpool"] = new
				{
					threads = ThreadPool.ThreadCount, pendingWorkItems = ThreadPool.PendingWorkItemCount,
					completedPerSecond = (long)((completed - _lastCompleted) / elapsed)
				},
				["runtime"] = new
				{
					version = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
					build = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
					uptimeSeconds = (long)(now - _started).TotalSeconds, cpus = Environment.ProcessorCount,
					logsDropped = LogsDropped(),
					environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"
				},
				["exceptions"] = new
				{
					perSecond = (exceptions - _lastExceptions) / elapsed, total = exceptions, recent = _recent.ToArray()
				},
				["http"] = _hosts.OrderBy(h => h.Key).ToDictionary(h => h.Key, h => h.Value.Snapshot(elapsed))
			};
			_lastCpu = cpu;
			_lastAllocated = allocated;
			_lastCompleted = completed;
			_lastExceptions = exceptions;
			_lastSample = now;
			return _cache = result;
		}
	}

	/// <summary>Forgets the counted exceptions and the list of recent ones.</summary>
	public void ResetExceptions()
	{
		Interlocked.Exchange(ref _exceptions, 0);
		_lastExceptions = 0;
		_recent.Clear();
	}

	/// <summary>Collects the garbage now, compacting the large object heap.</summary>
	public static void Collect()
	{
		GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
	}

	private void OnException(object? sender, FirstChanceExceptionEventArgs args)
	{
		Interlocked.Increment(ref _exceptions);
		_recent.Enqueue(args.Exception.GetType().Name);
		while (_recent.Count > RecentExceptions) _recent.TryDequeue(out _);
	}

	private void OnDuration(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags,
		object? state)
	{
		string host = "other", status = "2xx";
		foreach (var tag in tags)
			if (tag.Key == "host") host = tag.Value?.ToString() ?? host;
			else if (tag.Key == "status") status = tag.Value?.ToString() ?? status;

		// Whoever calls the meter, the windows stay bounded to the known labels.
		if (!HostLabels.All.Contains(host)) host = HostLabels.Other;
		_hosts.GetOrAdd(host, _ => new HostWindow()).Add(value, status);
	}

	private sealed class HostWindow
	{
		private readonly List<double> _durations = [];
		private readonly object _lock = new();
		private readonly Dictionary<string, int> _statuses = new();

		public void Add(double milliseconds, string status)
		{
			lock (_lock)
			{
				_durations.Add(milliseconds);
				_statuses[status] = _statuses.GetValueOrDefault(status) + 1;
			}
		}

		public object Snapshot(double seconds)
		{
			lock (_lock)
			{
				var sorted = _durations.Order().ToArray();
				var result = new
				{
					requestsPerSecond = sorted.Length / seconds, p50Milliseconds = Percentile(sorted, 0.5),
					p95Milliseconds = Percentile(sorted, 0.95), statuses = new Dictionary<string, int>(_statuses)
				};
				_durations.Clear();
				_statuses.Clear();
				return result;
			}
		}

		private static double Percentile(double[] sorted, double fraction)
		{
			return sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(fraction * sorted.Length))];
		}
	}
}