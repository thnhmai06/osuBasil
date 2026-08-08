using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Basil.LoadTests.Infrastructure.Metrics;

/// <summary>
///     Runs <c>dotnet-counters collect</c> as a sidecar against a known process id and tails its CSV
///     output for the GC-heap/gen-count/allocation-rate/threadpool fields <see cref="ProcessResourceSampler" />
///     cannot see. Missing from PATH degrades this to reporting empty samples plus one logged warning —
///     it never fails the run.
/// </summary>
public sealed class DotnetCountersSidecar(int processId, string outputCsvPath, int refreshIntervalSeconds,
	Action<string> logWarning) : IResourceSampler
{
	private static readonly string[] Counters =
	[
		"gc-heap-size", "gen-0-gc-count", "gen-1-gc-count", "gen-2-gc-count", "alloc-rate",
		"threadpool-thread-count"
	];

	private Process? _process;
	private bool _available;
	private bool _warnedOnce;

	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		var directory = Path.GetDirectoryName(outputCsvPath);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

		var arguments = $"collect -p {processId} --format csv -o \"{outputCsvPath}\" " +
		                $"--refresh-interval {refreshIntervalSeconds} " +
		                $"--counters System.Runtime[{string.Join(',', Counters)}]";

		try
		{
			_process = Process.Start(new ProcessStartInfo("dotnet-counters", arguments)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			});
			_available = _process is not null;
		}
		catch (Win32Exception)
		{
			_available = false;
			WarnOnce("dotnet-counters not found on PATH — GC heap, gen0/1/2 counts, allocation rate, and " +
			         "threadpool-thread-count will be reported as unavailable for this run.");
		}

		return Task.CompletedTask;
	}

	public Task<ResourceSample> SampleAsync(CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;
		if (!_available || !File.Exists(outputCsvPath)) return Task.FromResult(new ResourceSample(now));

		try
		{
			var latest = ReadLatestCounters(outputCsvPath);
			return Task.FromResult(new ResourceSample(now)
			{
				GcHeapBytes = ToBytesFromMb(latest, "gc-heap-size"),
				Gen0Collections = ToLong(latest, "gen-0-gc-count"),
				Gen1Collections = ToLong(latest, "gen-1-gc-count"),
				Gen2Collections = ToLong(latest, "gen-2-gc-count"),
				AllocRateBytesPerSecond = ToDouble(latest, "alloc-rate"),
				ThreadPoolThreads = (int?)ToLong(latest, "threadpool-thread-count")
			});
		}
		catch (IOException)
		{
			// The sidecar may be mid-write; skip this sample rather than fail the run.
			return Task.FromResult(new ResourceSample(now));
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		if (_process is { HasExited: false })
		{
			try
			{
				_process.Kill(true);
				await _process.WaitForExitAsync(cancellationToken);
			}
			catch (InvalidOperationException)
			{
				// Already exited between the check and the kill; nothing to do.
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync();
		_process?.Dispose();
	}

	private void WarnOnce(string message)
	{
		if (_warnedOnce) return;
		_warnedOnce = true;
		logWarning(message);
	}

	/// <summary>Reads the CSV's rows for the most recent timestamp into a counter-name -> value map.</summary>
	private static Dictionary<string, double> ReadLatestCounters(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream);

		var header = reader.ReadLine()?.Split(',') ?? [];
		var timestampIndex = Array.IndexOf(header, "Timestamp");
		var nameIndex = Array.IndexOf(header, "Counter Name");
		var valueIndex = Array.IndexOf(header, "Mean/Increment");
		if (timestampIndex < 0 || nameIndex < 0 || valueIndex < 0) return [];

		string? latestTimestamp = null;
		var latestRow = new Dictionary<string, double>();
		var pendingRow = new Dictionary<string, double>();

		while (reader.ReadLine() is { } line)
		{
			var cells = line.Split(',');
			if (cells.Length <= Math.Max(timestampIndex, Math.Max(nameIndex, valueIndex))) continue;

			var timestamp = cells[timestampIndex];
			if (timestamp != latestTimestamp)
			{
				if (latestTimestamp is not null) latestRow = pendingRow;
				pendingRow = [];
				latestTimestamp = timestamp;
			}

			if (double.TryParse(cells[valueIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
				pendingRow[cells[nameIndex]] = value;
		}

		return pendingRow.Count > 0 ? pendingRow : latestRow;
	}

	private static long? ToLong(IReadOnlyDictionary<string, double> counters, string name)
	{
		return counters.TryGetValue(name, out var value) ? (long)value : null;
	}

	private static double? ToDouble(IReadOnlyDictionary<string, double> counters, string name)
	{
		return counters.TryGetValue(name, out var value) ? value : null;
	}

	private static long? ToBytesFromMb(IReadOnlyDictionary<string, double> counters, string name)
	{
		return counters.TryGetValue(name, out var value) ? (long)(value * 1024 * 1024) : null;
	}
}
