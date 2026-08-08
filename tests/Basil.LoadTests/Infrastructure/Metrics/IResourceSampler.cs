namespace Basil.LoadTests.Infrastructure.Metrics;

/// <summary>
///     Samples one resource-usage source (a process, a container, an EventPipe counters collector). An
///     <see cref="Hosting.IServerHost" /> owns one or more of these and merges their samples in
///     <see cref="Hosting.IServerHost.CollectMetricsAsync" /> — samplers never duplicate each other's
///     fields.
/// </summary>
public interface IResourceSampler : IAsyncDisposable
{
	/// <summary>Starts the sampler (e.g. attaches an EventPipe session). A no-op for samplers with no setup.</summary>
	Task StartAsync(CancellationToken cancellationToken = default);

	/// <summary>Takes one sample. Fields this sampler cannot observe are left <see langword="null" />.</summary>
	Task<ResourceSample> SampleAsync(CancellationToken cancellationToken = default);

	/// <summary>Stops the sampler and releases any resources it holds.</summary>
	Task StopAsync(CancellationToken cancellationToken = default);
}
