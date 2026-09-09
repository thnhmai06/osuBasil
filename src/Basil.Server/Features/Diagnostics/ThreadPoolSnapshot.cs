namespace Basil.Server.Features.Diagnostics;

/// <summary>The thread pool's current sizing and workload counters.</summary>
/// <param name="ThreadCount">The number of threads currently in the pool.</param>
/// <param name="MinWorkerThreads">The configured minimum number of worker threads the pool keeps ready.</param>
/// <param name="MaxWorkerThreads">The configured maximum number of worker threads the pool may create.</param>
/// <param name="AvailableWorkerThreads">The number of additional worker threads the pool could still start.</param>
/// <param name="PendingWorkItemCount">The number of work items queued but not yet started.</param>
/// <param name="CompletedWorkItemCount">The cumulative number of work items completed since the process started.</param>
/// <param name="LockContentionCount">The cumulative number of times a thread had to wait to enter a monitor lock since the process started.</param>
public sealed record ThreadPoolSample(
	int ThreadCount,
	int MinWorkerThreads,
	int MaxWorkerThreads,
	int AvailableWorkerThreads,
	long PendingWorkItemCount,
	long CompletedWorkItemCount,
	long LockContentionCount);

/// <summary>Reads the thread pool's current state from its static counters.</summary>
/// <remarks>
///     None of these counters require a running listener; every one of them is already a plain,
///     always-maintained static property on the runtime.
///
///     Deliberately excludes the legacy I/O completion port thread counts
///     (<c>ThreadPool.GetMinThreads</c>/<c>GetMaxThreads</c>/<c>GetAvailableThreads</c>'s
///     <c>completionPortThreads</c> out-parameter): on the portable thread pool the runtime uses by
///     default, those are fixed legacy compatibility values that never move with actual I/O load, so
///     reporting them would invite a reader to draw conclusions from a number that carries no signal.
/// </remarks>
public static class ThreadPoolSnapshot
{
	/// <summary>Takes a fresh reading of the thread pool's current state.</summary>
	public static ThreadPoolSample Capture()
	{
		ThreadPool.GetMinThreads(out var minWorkerThreads, out _);
		ThreadPool.GetMaxThreads(out var maxWorkerThreads, out _);
		ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out _);

		return new ThreadPoolSample(
			ThreadPool.ThreadCount,
			minWorkerThreads,
			maxWorkerThreads,
			availableWorkerThreads,
			ThreadPool.PendingWorkItemCount,
			ThreadPool.CompletedWorkItemCount,
			Monitor.LockContentionCount);
	}
}
