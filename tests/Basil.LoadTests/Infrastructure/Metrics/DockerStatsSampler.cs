using Docker.DotNet;
using Docker.DotNet.Models;

namespace Basil.LoadTests.Infrastructure.Metrics;

/// <summary>
///     Samples CPU% and memory usage for a container via the Docker Engine API (no <c>docker stats</c>
///     CLI spawn). Docker's own stats surface has no GC/thread/handle view into the container, so those
///     fields are always left <see langword="null" /> here — <see cref="Hosting.DockerServerHost" />'s
///     <see cref="Hosting.ServerHostCapabilities" /> reflects that rather than guessing.
/// </summary>
/// <param name="client">A client for the Docker daemon.</param>
/// <param name="containerId">The container id or name to query.</param>
public sealed class DockerStatsSampler(DockerClient client, string containerId) : IResourceSampler
{
	public Task StartAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public async Task<ResourceSample> SampleAsync(CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;

		ContainerStatsResponse? stats;
		try
		{
			stats = await GetOneShotStatsAsync(cancellationToken);
		}
		catch (DockerApiException)
		{
			// The container may have just stopped mid-request; skip this sample rather than fail the run.
			return new ResourceSample(now);
		}

		if (stats is null) return new ResourceSample(now);

		return new ResourceSample(now)
		{
			CpuPercent = ComputeCpuPercent(stats),
			WorkingSetBytes = stats.MemoryStats?.Usage is { } usage ? (long)usage : null
		};
	}

	public Task StopAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}

	private async Task<ContainerStatsResponse?> GetOneShotStatsAsync(CancellationToken cancellationToken)
	{
		// A one-shot request makes the daemon send exactly one snapshot, delivered through the progress
		// callback rather than the returned task — so capture it via the callback and await both.
		var completion = new TaskCompletionSource<ContainerStatsResponse?>();
		var progress = new Progress<ContainerStatsResponse>(stats => completion.TrySetResult(stats));

		await client.Containers.GetContainerStatsAsync(containerId, new ContainerStatsParameters
		{
			Stream = false,
			OneShot = true
		}, progress, cancellationToken);

		return await completion.Task.WaitAsync(cancellationToken);
	}

	private static double? ComputeCpuPercent(ContainerStatsResponse stats)
	{
		var cpuDelta = (stats.CPUStats?.CPUUsage?.TotalUsage ?? 0) - (stats.PreCPUStats?.CPUUsage?.TotalUsage ?? 0);
		var systemDelta = (stats.CPUStats?.SystemUsage ?? 0) - (stats.PreCPUStats?.SystemUsage ?? 0);
		if (cpuDelta <= 0 || systemDelta <= 0) return null;

		var onlineCpus = stats.CPUStats?.OnlineCPUs ?? 1;
		return (double)cpuDelta / systemDelta * onlineCpus * 100.0;
	}
}