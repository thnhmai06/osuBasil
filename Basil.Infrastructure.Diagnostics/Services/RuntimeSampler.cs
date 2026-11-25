using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Basil.Application.Diagnostics.Services;

namespace Basil.Infrastructure.Diagnostics.Services;

internal sealed class RuntimeSampler : IDisposable, IClearTally
{
	private const int RecentExceptions = 20;
	private readonly TimeProvider _clock;
	private readonly ConcurrentDictionary<string, HostWindow> _hosts = new();
	private readonly HostWindow _irc = new();
	private readonly MeterListener _listener = new();
	private readonly ConcurrentQueue<string> _recent = new();
	private readonly HostWindow _stable = new();
	private readonly DateTimeOffset _started;
	private readonly object _sync = new();
	private Dictionary<string, object> _cache = new();
	private long _exceptions;
	private long _lastAllocated;
	private long _lastCompleted;
	private TimeSpan _lastCpu;
	private long _lastExceptions;
	private DateTimeOffset _lastSample;

	public RuntimeSampler(TimeProvider clock)
	{
		_clock = clock;
		_started = clock.GetUtcNow();
		_lastSample = _started;
		AppDomain.CurrentDomain.FirstChanceException += OnException;
		// The hosts own the instrument; it is found by its agreed name wherever it is published.
		_listener.InstrumentPublished = (instrument, listener) =>
		{
			if (instrument.Name is DiagnosticsMetrics.HttpDurationName or DiagnosticsMetrics.StableDurationName
			    or DiagnosticsMetrics.IrcDurationName) listener.EnableMeasurementEvents(instrument);
		};
		_listener.SetMeasurementEventCallback<double>(OnDuration);
		_listener.Start();
	}

	public Func<long> LogsDropped { get; set; } = () => 0;

	/// <summary>Told, at every new sample, how loaded the server is: the larger of the CPU and memory use, from 0 to 1.</summary>
	public Action<double>? LoadMeasured { get; set; }

	public void Reset()
	{
		Interlocked.Exchange(ref _exceptions, 0);
		_lastExceptions = 0;
		_recent.Clear();
	}

	public void Dispose()
	{
		AppDomain.CurrentDomain.FirstChanceException -= OnException;
		_listener.Dispose();
	}

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
				["http"] = _hosts.OrderBy(h => h.Key).ToDictionary(h => h.Key, h => (object)h.Value.Snapshot(elapsed)),
				["stable"] = _stable.Snapshot(elapsed, false),
				["irc"] = _irc.Snapshot(elapsed, false)
			};
			LoadMeasured?.Invoke(LoadOf(cpu - _lastCpu, elapsed, process.WorkingSet64, gc.TotalAvailableMemoryBytes));
			_lastCpu = cpu;
			_lastAllocated = allocated;
			_lastCompleted = completed;
			_lastExceptions = exceptions;
			_lastSample = now;
			return _cache = result;
		}
	}

	private static double LoadOf(TimeSpan cpuUsed, double seconds, long workingSet, long availableMemory)
	{
		var cpu = cpuUsed.TotalSeconds / seconds / Environment.ProcessorCount;
		var memory = availableMemory > 0 ? (double)workingSet / availableMemory : 0;
		// Not the thread pool queue: read at one instant it jumps with every burst of polls, and the value stands until the
		// next sample, so the server shed every poll for a while at a tenth of its CPU.
		return Math.Clamp(Math.Max(cpu, memory), 0, 1);
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
		if (instrument.Name == DiagnosticsMetrics.StableDurationName)
		{
			_stable.Add(value, "2xx");
			return;
		}

		if (instrument.Name == DiagnosticsMetrics.IrcDurationName)
		{
			_irc.Add(value, "2xx");
			return;
		}

		string host = "other", status = "2xx";
		foreach (var tag in tags)
			if (tag.Key == DiagnosticsMetrics.HostTag) host = tag.Value?.ToString() ?? host;
			else if (tag.Key == DiagnosticsMetrics.StatusTag) status = tag.Value?.ToString() ?? status;

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

		public Dictionary<string, object> Snapshot(double seconds, bool withStatuses = true)
		{
			lock (_lock)
			{
				var sorted = _durations.Order().ToArray();
				var result = new Dictionary<string, object>
				{
					["requestsPerSecond"] = sorted.Length / seconds, ["p50Milliseconds"] = Percentile(sorted, 0.5),
					["p95Milliseconds"] = Percentile(sorted, 0.95), ["p99Milliseconds"] = Percentile(sorted, 0.99)
				};
				if (withStatuses) result["statuses"] = new Dictionary<string, int>(_statuses);
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