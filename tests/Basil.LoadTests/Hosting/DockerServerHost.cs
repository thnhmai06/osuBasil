using System.Diagnostics;
using System.Net;
using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.LoadTests.Helpers;
using Basil.LoadTests.Infrastructure.Metrics;

namespace Basil.LoadTests.Hosting;

/// <summary>
///     Runs Basil via <c>docker compose</c>, using the repo's own <c>docker-compose.yml</c> (or a
///     profile-supplied compose file). GC/allocation counters are not available for a containerized
///     server — reaching them would mean an in-container <c>dotnet-counters</c> sidecar via
///     <c>docker exec</c>, which this implementation does not attempt — so <see cref="Capabilities" />
///     reports them as unavailable rather than approximating.
/// </summary>
/// <remarks>
///     This host could not be exercised against a real Docker daemon in the environment this was
///     written in (no daemon was running); it is implemented against the documented compose shape but
///     unverified end-to-end.
/// </remarks>
public sealed class DockerServerHost(ServerHostSettings settings) : IServerHost
{
	private readonly string _composeFile = RepoPaths.Resolve(settings.Docker.ComposeFile);
	private readonly string _repoRoot = RepoPaths.RepoRoot;
	private string? _containerId;
	private BasilHttpClientFactory? _clientFactory;

	public ServerHostCapabilities Capabilities { get; } = new(
		CanMeasureStartupTime: true,
		CanMeasureProcessMetrics: true, // CPU + working set only, via `docker stats`
		CanMeasureDotnetCounters: false,
		CanSnapshotDatabase: true); // via the ./docker-data/Data bind mount

	public ServerEndpoint Endpoint { get; } = new(settings.Domain, settings.Port, IPAddress.Loopback);

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		await RunComposeAsync($"up -d {settings.Docker.ServiceName}", cancellationToken);
		_containerId = (await RunComposeForOutputAsync($"ps -q {settings.Docker.ServiceName}", cancellationToken))
			.Trim();
		_clientFactory = new BasilHttpClientFactory(Endpoint, new ClientSettings());
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		await RunComposeAsync($"stop {settings.Docker.ServiceName}", cancellationToken);
		_clientFactory?.Dispose();
		_clientFactory = null;
	}

	public async Task<TimeSpan?> WaitUntilHealthyAsync(CancellationToken cancellationToken = default)
	{
		if (_clientFactory is null)
			throw new InvalidOperationException($"{nameof(StartAsync)} must complete before waiting for health.");

		return await ServerReadinessProbe.WaitAsync(_clientFactory, settings.StartupTimeout, cancellationToken);
	}

	public async Task<ResourceSample> CollectMetricsAsync(CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(_containerId)) return new ResourceSample(DateTimeOffset.UtcNow);

		await using var sampler = new DockerStatsSampler(_containerId);
		return await sampler.SampleAsync(cancellationToken);
	}

	public async Task ExportResultsAsync(string reportFolder, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(_containerId)) return;

		Directory.CreateDirectory(reportFolder);
		var logs = await RunProcessForOutputAsync("docker", $"logs --tail 500 {_containerId}", cancellationToken);
		await File.WriteAllTextAsync(Path.Combine(reportFolder, "server-log-tail.txt"), logs, cancellationToken);
	}

	public Task<bool> SyncDatabaseSnapshotAsync(string snapshotPath, bool restoreIfPresent,
		CancellationToken cancellationToken = default)
	{
		var bindMountRoot = RepoPaths.Resolve("docker-data");
		return Task.FromResult(ServerDatabase.SyncSnapshot(bindMountRoot, snapshotPath, restoreIfPresent));
	}

	public ValueTask DisposeAsync()
	{
		_clientFactory?.Dispose();
		return ValueTask.CompletedTask;
	}

	private Task RunComposeAsync(string arguments, CancellationToken cancellationToken)
	{
		return RunProcessAsync("docker", $"compose -f \"{_composeFile}\" {arguments}", _repoRoot, cancellationToken);
	}

	private Task<string> RunComposeForOutputAsync(string arguments, CancellationToken cancellationToken)
	{
		return RunProcessForOutputAsync("docker", $"compose -f \"{_composeFile}\" {arguments}", cancellationToken,
			_repoRoot);
	}

	private static async Task RunProcessAsync(string fileName, string arguments, string workingDirectory,
		CancellationToken cancellationToken)
	{
		using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
		{
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		}) ?? throw new InvalidOperationException($"Failed to start '{fileName} {arguments}'.");

		var error = await process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);
		if (process.ExitCode != 0)
			throw new InvalidOperationException($"'{fileName} {arguments}' failed (exit {process.ExitCode}): {error}");
	}

	private static Task<string> RunProcessForOutputAsync(string fileName, string arguments,
		CancellationToken cancellationToken, string? workingDirectory = null)
	{
		return RunProcessForOutputCoreAsync(fileName, arguments, workingDirectory, cancellationToken);
	}

	private static async Task<string> RunProcessForOutputCoreAsync(string fileName, string arguments,
		string? workingDirectory, CancellationToken cancellationToken)
	{
		using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
		{
			WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		}) ?? throw new InvalidOperationException($"Failed to start '{fileName} {arguments}'.");

		var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken);
		return output;
	}
}
