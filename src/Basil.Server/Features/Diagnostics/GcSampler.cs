using System.Diagnostics;
using System.Runtime;

namespace Basil.Server.Features.Diagnostics;

/// <summary>The garbage collector's cumulative activity and its most recent heap shape.</summary>
/// <param name="Gen0Collections">The cumulative number of generation-0 collections since the process started.</param>
/// <param name="Gen1Collections">The cumulative number of generation-1 collections since the process started.</param>
/// <param name="Gen2Collections">The cumulative number of generation-2 collections since the process started.</param>
/// <param name="Gen0SizeBytes">The generation-0 heap size as of the most recently completed collection.</param>
/// <param name="Gen1SizeBytes">The generation-1 heap size as of the most recently completed collection.</param>
/// <param name="Gen2SizeBytes">The generation-2 heap size as of the most recently completed collection.</param>
/// <param name="LargeObjectHeapSizeBytes">The large object heap's size as of the most recently completed collection.</param>
/// <param name="PinnedObjectHeapSizeBytes">The pinned object heap's size as of the most recently completed collection.</param>
/// <param name="FragmentedBytes">The heap's total fragmentation as of the most recently completed collection.</param>
/// <param name="IsServerGc">Whether the process is running the server garbage collector, fixed for its whole lifetime.</param>
/// <param name="IsConcurrentGcEnabled">Whether background (concurrent) collection is configured, fixed for the process's whole lifetime.</param>
/// <param name="LatencyMode">The garbage collector's current latency mode.</param>
/// <param name="TotalPauseDuration">The cumulative time the process has spent paused for collection since it started.</param>
/// <param name="TimeInGcPercent">
///     The share of wall-clock time spent paused for collection since the previous sample, smoothed
///     over that interval; zero on the first sample taken from a given <see cref="GcSampler" />.
/// </param>
/// <param name="TotalAllocatedBytes">The cumulative number of bytes allocated on the managed heap since the process started.</param>
/// <remarks>
///     <see cref="Gen0SizeBytes" />, <see cref="Gen1SizeBytes" />, <see cref="Gen2SizeBytes" />,
///     <see cref="LargeObjectHeapSizeBytes" />, <see cref="PinnedObjectHeapSizeBytes" /> and
///     <see cref="FragmentedBytes" /> all reflect the heap as of the last completed collection rather
///     than this exact instant, and read zero before the process's first collection has run.
/// </remarks>
public sealed record GcSample(
	long Gen0Collections,
	long Gen1Collections,
	long Gen2Collections,
	long Gen0SizeBytes,
	long Gen1SizeBytes,
	long Gen2SizeBytes,
	long LargeObjectHeapSizeBytes,
	long PinnedObjectHeapSizeBytes,
	long FragmentedBytes,
	bool IsServerGc,
	bool IsConcurrentGcEnabled,
	GCLatencyMode LatencyMode,
	TimeSpan TotalPauseDuration,
	double TimeInGcPercent,
	long TotalAllocatedBytes);

/// <summary>Samples the garbage collector's cumulative counters and most recent heap shape.</summary>
/// <remarks>
///     Every field this type reads is either a plain static property or bundled into the same
///     already-cached <see cref="GCMemoryInfo" /> struct the runtime keeps regardless of whether
///     anything ever reads it, so unlike <see cref="ProcessSampler" /> there is no shared handle to
///     hold or refresh here.
///
///     <see cref="GCMemoryInfo.PauseTimePercentage" /> describes only the single most recent
///     collection, not an ongoing rate, so it would misrepresent "time in GC" as a rolling figure.
///     <see cref="GcSample.TimeInGcPercent" /> is derived instead from the change in
///     <see cref="GC.GetTotalPauseDuration" /> between samples, smoothed over the interval between
///     them.
///
///     The large object and pinned object heap sizes come from <see cref="GCMemoryInfo.GenerationInfo" />
///     slots located relative to <see cref="GC.MaxGeneration" /> rather than a literal index, because
///     the position of those two slots in the array is an artifact of how many ordinary generations
///     the collector is using, not a fixed constant.
/// </remarks>
public sealed class GcSampler
{
	private static readonly bool ConcurrentGcEnabled = ReadConcurrentGcConfiguration();

	private TimeSpan? _lastPauseDuration;
	private long _lastPauseTimestamp;

	/// <summary>Takes a fresh sample of the garbage collector's counters and heap shape.</summary>
	public GcSample Sample()
	{
		var info = GC.GetGCMemoryInfo();
		var generations = info.GenerationInfo;
		var maxGeneration = GC.MaxGeneration;

		var now = Stopwatch.GetTimestamp();
		var pauseDuration = GC.GetTotalPauseDuration();
		var timeInGcPercent = ComputeTimeInGcPercent(now, pauseDuration);

		return new GcSample(
			GC.CollectionCount(0),
			GC.CollectionCount(1),
			GC.CollectionCount(2),
			GenerationSize(generations, 0),
			GenerationSize(generations, 1),
			GenerationSize(generations, maxGeneration),
			GenerationSize(generations, maxGeneration + 1),
			GenerationSize(generations, maxGeneration + 2),
			info.FragmentedBytes,
			GCSettings.IsServerGC,
			ConcurrentGcEnabled,
			GCSettings.LatencyMode,
			pauseDuration,
			timeInGcPercent,
			GC.GetTotalAllocatedBytes(false));
	}

	private double ComputeTimeInGcPercent(long now, TimeSpan pauseDuration)
	{
		var timeInGcPercent = 0d;

		if (_lastPauseDuration is { } lastPauseDuration)
		{
			var wallElapsed = Stopwatch.GetElapsedTime(_lastPauseTimestamp, now);
			var pauseElapsed = pauseDuration - lastPauseDuration;
			if (wallElapsed > TimeSpan.Zero)
				timeInGcPercent = pauseElapsed.TotalMilliseconds / wallElapsed.TotalMilliseconds * 100;
		}

		_lastPauseDuration = pauseDuration;
		_lastPauseTimestamp = now;
		return timeInGcPercent;
	}

	private static long GenerationSize(ReadOnlySpan<GCGenerationInfo> generations, int index) =>
		index >= 0 && index < generations.Length ? generations[index].SizeAfterBytes : 0;

	/// <summary>
	///     Reads whether concurrent (background) collection is configured, once for the process's
	///     lifetime: the source dictionary allocates on every call, so it must not be read per sample.
	/// </summary>
	private static bool ReadConcurrentGcConfiguration()
	{
		var variables = GC.GetConfigurationVariables();
		return variables.TryGetValue("ConcurrentGC", out var value) && value is true;
	}
}
