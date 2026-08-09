using System.Diagnostics.Tracing;
using System.Globalization;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

namespace Basil.LoadTests.Infrastructure.Metrics;

/// <summary>
///     Collects the GC-heap/gen-count/allocation-rate/threadpool counters <see cref="ProcessResourceSampler" />
///     cannot see by attaching an EventPipe session to a known process id via the diagnostics client
///     library — no <c>dotnet-counters</c> executable on PATH, no sidecar process, no CSV to parse.
///     A failed attachment (targets not ready, diagnostics disabled) degrades this to empty samples plus one
///     logged warning — it never fails the run.
/// </summary>
/// <param name="processId">The process id to attach to.</param>
/// <param name="sampleIntervalSeconds">How often the runtime publishes counter-events.</param>
/// <param name="logWarning">Sink for the single non-fatal degradation warning.</param>
public sealed class DotnetRuntimeMetricsCollector(int processId, int sampleIntervalSeconds,
	Action<string> logWarning) : IResourceSampler
{
	private readonly Lock _stateLock = new();
	private readonly Dictionary<string, double> _latest = new();
	private readonly List<EventPipeProvider> _providers =
	[
		new("System.Runtime", EventLevel.Informational, 0,
			new Dictionary<string, string>
			{
				["EventCounterIntervalSec"] = sampleIntervalSeconds.ToString(CultureInfo.InvariantCulture)
			})
	];

	private EventPipeSession? _session;
	private Task? _processingTask;
	private bool _warnedOnce;

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		// The target may still be booting when StartAsync runs; retry briefly rather than warn on a
		// transient attach failure.
		for (var attempt = 0; attempt < 5; attempt++)
		{
			try
			{
				_session = new DiagnosticsClient(processId).StartEventPipeSession(_providers,
					requestRundown: false);
				break;
			}
			catch (DiagnosticsClientException)
			{
				// The target runtime's diagnostics channel is not ready (or disabled); retry then degrade.
			}
			catch (EndOfStreamException)
			{
				// Same transient attach race; retry then degrade.
			}

			if (attempt == 4)
			{
				WarnOnce("The target process does not expose a ready EventPipe channel — GC heap, gen0/1/2 " +
				         "counts, allocation rate, and threadpool-thread-count will be reported as " +
				         "unavailable for this run.");
				return;
			}

			await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
		}

		if (_session is null)
		{
			WarnOnce("The target process never became attachable — GC heap, gen0/1/2 counts, allocation " +
			         "rate, and threadpool-thread-count will be reported as unavailable for this run.");
			return;
		}

		_processingTask = Task.Run(ProcessEvents, cancellationToken);
	}

	public Task<ResourceSample> SampleAsync(CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;

		lock (_stateLock)
		{
			if (_latest.Count == 0) return Task.FromResult(new ResourceSample(now));

			return Task.FromResult(new ResourceSample(now)
			{
				GcHeapBytes = ToBytesFromMb(_latest, "gc-heap-size"),
				Gen0Collections = ToLong(_latest, "gen-0-gc-count"),
				Gen1Collections = ToLong(_latest, "gen-1-gc-count"),
				Gen2Collections = ToLong(_latest, "gen-2-gc-count"),
				AllocRateBytesPerSecond = ToDouble(_latest, "alloc-rate"),
				ThreadPoolThreads = (int?)ToLong(_latest, "threadpool-thread-count")
			});
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		var session = _session;
		_session = null;

		if (session is not null)
		{
			try
			{
				await session.StopAsync(cancellationToken);
			}
			catch (EndOfStreamException)
			{
				// The session already ended on its own; nothing to stop.
			}
			catch (ServerNotAvailableException)
			{
				// The target process exited before the session could be stopped; the reader observes
				// the stream end on its own (handled in ProcessEvents) and collection just stops.
			}
			session.Dispose();
		}

		if (_processingTask is not null)
		{
			try
			{
				await _processingTask.WaitAsync(cancellationToken);
			}
			catch (OperationCanceledException)
			{
				// The caller is shutting down; the event-processing task is a background daemon.
			}
			_processingTask = null;
		}
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync();
	}

	private void ProcessEvents()
	{
		try
		{
			using var source = new EventPipeEventSource(_session!.EventStream);
			source.Dynamic.All += OnTraceEvent;
			source.Process();
		}
		catch (EndOfStreamException)
		{
			// Expected when the session is stopped or the target process exits.
		}
		catch (IOException)
		{
			// The target process exited mid-read; the collector simply stops receiving events.
		}
		catch (ObjectDisposedException)
		{
			// Expected when the session is disposed while the reader is blocked (see StopAsync).
		}
		catch (FormatException)
		{
			// A truncated stream (the target process exited mid-block) surfaces as a format error
			// rather than a clean EOF; treat it as the same benign end of collection.
		}
	}

	private void OnTraceEvent(TraceEvent traceEvent)
	{
		if (!string.Equals(traceEvent.EventName, "EventCounters", StringComparison.Ordinal)) return;
		if (traceEvent.PayloadValue(0) is not IDictionary<string, object> payload) return;
		if (!payload.TryGetValue("Payload", out var payloadObj) ||
		    payloadObj is not IDictionary<string, object> fields) return;
		if (!fields.TryGetValue("Name", out var nameObj) || nameObj is not string name) return;

		// Gauge counters carry the value in Mean; incrementing counters (alloc-rate, gen counts) in Increment.
		double? value = fields.TryGetValue("Mean", out var meanObj) && meanObj is double mean ? mean
			: fields.TryGetValue("Increment", out var incrementObj) && incrementObj is double increment ? increment
			: null;
		if (value is null) return;

		lock (_stateLock)
		{
			_latest[name] = value.Value;
		}
	}

	private void WarnOnce(string message)
	{
		if (_warnedOnce) return;
		_warnedOnce = true;
		logWarning(message);
	}

	private static long? ToLong(Dictionary<string, double> counters, string name)
	{
		return counters.TryGetValue(name, out var value) ? (long)value : null;
	}

	private static double? ToDouble(Dictionary<string, double> counters, string name)
	{
		return counters.TryGetValue(name, out var value) ? value : null;
	}

	private static long? ToBytesFromMb(Dictionary<string, double> counters, string name)
	{
		return counters.TryGetValue(name, out var value) ? (long)(value * 1024 * 1024) : null;
	}
}
