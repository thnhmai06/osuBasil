using System.Diagnostics;

namespace Basil.Server.Features.Diagnostics;

/// <summary>One process's live resource usage and the runtime's current memory pressure.</summary>
/// <param name="ProcessId">The operating system process id, fixed for the process's lifetime.</param>
/// <param name="Uptime">How long the process has been running.</param>
/// <param name="WorkingSetBytes">The process's current physical memory footprint.</param>
/// <param name="PrivateMemoryBytes">The memory allocated for the process that cannot be shared with other processes.</param>
/// <param name="PeakWorkingSetBytes">The highest working-set value the process has reached; only ever grows.</param>
/// <param name="CpuUsagePercent">
///     The share of available CPU time the process consumed since the previous sample, across every
///     core; zero on the first sample taken from a given <see cref="ProcessSampler" />.
/// </param>
/// <param name="ThreadCount">The number of operating system threads in the process.</param>
/// <param name="HandleCount">The number of open handles the process holds.</param>
/// <param name="ManagedHeapBytes">The managed heap's estimated size, without forcing a collection first.</param>
/// <param name="GcHeapSizeBytes">The managed heap's total size as of the most recently completed garbage collection.</param>
/// <param name="FragmentedBytes">The managed heap's fragmentation as of the most recently completed garbage collection.</param>
/// <param name="MemoryLoadBytes">The whole system's (or container's) memory in use as of the most recently completed garbage collection.</param>
/// <param name="TotalAvailableMemoryBytes">The memory limit the garbage collector is budgeting against.</param>
/// <param name="HighMemoryLoadThresholdBytes">The memory-load level above which the garbage collector becomes more aggressive.</param>
/// <remarks>
///     <see cref="GcHeapSizeBytes" />, <see cref="FragmentedBytes" /> and <see cref="MemoryLoadBytes" />
///     reflect the state as of the last completed garbage collection rather than this exact instant,
///     and read zero before the process's first collection has run.
/// </remarks>
public sealed record ProcessSample(
	int ProcessId,
	TimeSpan Uptime,
	long WorkingSetBytes,
	long PrivateMemoryBytes,
	long PeakWorkingSetBytes,
	double CpuUsagePercent,
	int ThreadCount,
	int HandleCount,
	long ManagedHeapBytes,
	long GcHeapSizeBytes,
	long FragmentedBytes,
	long MemoryLoadBytes,
	long TotalAvailableMemoryBytes,
	long HighMemoryLoadThresholdBytes);

/// <summary>Samples the current process's resource usage and the runtime's memory pressure.</summary>
/// <remarks>
///     Holds one <see cref="Process" /> handle for its own lifetime and refreshes it exactly once per
///     <see cref="Sample" /> call: obtaining a fresh handle or refreshing an existing one is a
///     syscall costing several milliseconds, enough on its own to blow past a one-second broadcast
///     budget if paid more than once per tick. Every process-derived field this type reports comes
///     from that single refresh.
///
///     <see cref="ProcessSample.TotalAvailableMemoryBytes" /> and
///     <see cref="ProcessSample.HighMemoryLoadThresholdBytes" /> track the host or container's memory
///     configuration, which changes essentially never while the process is running, so this type
///     refreshes them on a slower cadence than the rest of the sample instead of on every call.
/// </remarks>
public sealed class ProcessSampler
{
	private static readonly TimeSpan SlowFieldRefreshInterval = TimeSpan.FromMinutes(1);

	private readonly Process _process = Process.GetCurrentProcess();

	private TimeSpan? _lastCpuTime;
	private long _lastCpuTimestamp;

	private long _totalAvailableMemoryBytes;
	private long _highMemoryLoadThresholdBytes;
	private long? _lastSlowFieldRefreshTimestamp;

	/// <summary>How many times <see cref="Sample" /> has refreshed the held process handle.</summary>
	internal int RefreshCountForTest { get; private set; }

	/// <summary>Takes a fresh sample of the current process and the runtime's memory state.</summary>
	public ProcessSample Sample()
	{
		_process.Refresh();
		RefreshCountForTest++;

		var now = Stopwatch.GetTimestamp();
		var cpuUsagePercent = ComputeCpuUsagePercent(now);

		var gcInfo = GC.GetGCMemoryInfo();
		RefreshSlowFieldsIfDue(now, gcInfo);

		return new ProcessSample(
			Environment.ProcessId,
			DateTime.UtcNow - _process.StartTime.ToUniversalTime(),
			Environment.WorkingSet,
			_process.PrivateMemorySize64,
			_process.PeakWorkingSet64,
			cpuUsagePercent,
			_process.Threads.Count,
			_process.HandleCount,
			GC.GetTotalMemory(false),
			gcInfo.HeapSizeBytes,
			gcInfo.FragmentedBytes,
			gcInfo.MemoryLoadBytes,
			_totalAvailableMemoryBytes,
			_highMemoryLoadThresholdBytes);
	}

	private double ComputeCpuUsagePercent(long now)
	{
		var cpuTime = _process.TotalProcessorTime;
		var cpuUsagePercent = 0d;

		if (_lastCpuTime is { } lastCpuTime)
		{
			var wallElapsed = Stopwatch.GetElapsedTime(_lastCpuTimestamp, now);
			var cpuElapsed = cpuTime - lastCpuTime;
			if (wallElapsed > TimeSpan.Zero)
				cpuUsagePercent = cpuElapsed.TotalMilliseconds / wallElapsed.TotalMilliseconds
					/ Environment.ProcessorCount * 100;
		}

		_lastCpuTime = cpuTime;
		_lastCpuTimestamp = now;
		return cpuUsagePercent;
	}

	private void RefreshSlowFieldsIfDue(long now, GCMemoryInfo gcInfo)
	{
		if (_lastSlowFieldRefreshTimestamp is { } last &&
			Stopwatch.GetElapsedTime(last, now) < SlowFieldRefreshInterval)
			return;

		_totalAvailableMemoryBytes = gcInfo.TotalAvailableMemoryBytes;
		_highMemoryLoadThresholdBytes = gcInfo.HighMemoryLoadThresholdBytes;
		_lastSlowFieldRefreshTimestamp = now;
	}
}
