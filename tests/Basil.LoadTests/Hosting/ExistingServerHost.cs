using System.Net;
using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.LoadTests.Infrastructure.Metrics;

namespace Basil.LoadTests.Hosting;

/// <summary>
///     Attaches to an already-running Basil instance the harness does not own. Every lifecycle method
///     is a no-op except health/metrics collections: no start, no stop, and
///     <see cref="SyncDatabaseSnapshotAsync" /> refuses outright rather than touching data this host
///     didn't create. Process/counters metrics require an explicit <c>Existing.ProcessId</c> in the
///     profile — there is no process-name guessing.
/// </summary>
public sealed class ExistingServerHost(
	ServerHostSettings settings,
	DotnetCountersSettings countersSettings,
	Action<string> logWarning)
	: IServerHost
{
	private ProcessResourceSampler? _processSampler;
	private DotnetRuntimeMetricsCollector? _countersSampler;
	private BasilHttpClientFactory? _clientFactory;

	public ServerHostCapabilities Capabilities => new(
		CanMeasureStartupTime: false,
		CanMeasureProcessMetrics: settings.Existing.ProcessId.HasValue,
		CanMeasureDotnetCounters: settings.Existing.ProcessId.HasValue,
		CanSnapshotDatabase: false);

	public ServerEndpoint Endpoint { get; } = new(settings.Domain, settings.Port,
		IPAddress.Parse(settings.Existing.HostAddress));

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		_clientFactory = new BasilHttpClientFactory(Endpoint, new ClientSettings());

		if (settings.Existing.ProcessId is { } pid)
		{
			_processSampler = new ProcessResourceSampler(pid, settings.Port);

			if (countersSettings.Enabled)
			{
				_countersSampler = new DotnetRuntimeMetricsCollector(pid,
					countersSettings.RefreshIntervalSeconds, logWarning);
				await _countersSampler.StartAsync(cancellationToken);
			}
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		if (_countersSampler is not null) await _countersSampler.StopAsync(cancellationToken);
		_clientFactory?.Dispose();
		_clientFactory = null;
	}

	public async Task<TimeSpan?> WaitUntilHealthyAsync(CancellationToken cancellationToken = default)
	{
		if (_clientFactory is null)
			throw new InvalidOperationException($"{nameof(StartAsync)} must complete before waiting for health.");

		await ServerReadinessProbe.WaitAsync(_clientFactory, settings.StartupTimeout, cancellationToken);
		return null; // this host did not start the server, so no startup time can be attributed
	}

	public async Task<ResourceSample> CollectMetricsAsync(CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;
		if (_processSampler is null) return new ResourceSample(now);

		var processSample = await _processSampler.SampleAsync(cancellationToken);
		if (_countersSampler is null) return processSample;

		var countersSample = await _countersSampler.SampleAsync(cancellationToken);
		return processSample.MergeWith(countersSample);
	}

	public Task ExportResultsAsync(string reportFolder, CancellationToken cancellationToken = default)
	{
		// This host owns nothing of the server's — no log files, no counters process it started
		// beyond what CollectMetricsAsync already folded into the timeline.
		return Task.CompletedTask;
	}

	public Task<bool> SyncDatabaseSnapshotAsync(string snapshotPath, bool restoreIfPresent)
	{
		throw new NotSupportedException(
			"ExistingServerHost does not own the target server's data and refuses to snapshot or restore it.");
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync();
		if (_countersSampler is not null) await _countersSampler.DisposeAsync();
		if (_processSampler is not null) await _processSampler.DisposeAsync();
		_clientFactory?.Dispose();
	}
}
