using System.Diagnostics;
using System.Net;
using System.Text;
using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.LoadTests.Helpers;
using Basil.LoadTests.Infrastructure.Metrics;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Basil.LoadTests.Hosting;

/// <summary>
///     Runs Basil via <c>docker compose</c>, using the repo's own <c>docker-compose.yml</c> (or a
///     profile-supplied composition file). GC/allocation counters are not available for a containerized
///     server — reaching them would mean an in-container collector via <c>docker exec</c>, which this
///     implementation does not attempt — so <see cref="Capabilities" /> reports them as unavailable
///     rather than approximating.
/// </summary>
/// <remarks>
///     Compose has no .NET library (it is a CLI layer above the Docker Engine API), so starting the
///     service still shells out to <c>docker compose up</c>; everything else — container lookup, stop,
///     logs, stats — goes through <see cref="DockerClient" /> instead of the <c>docker</c> CLI.
/// </remarks>
public sealed class DockerServerHost(ServerHostSettings settings) : IServerHost
{
	private const string ComposeServiceLabel = "com.docker.compose.service";

	private readonly string _composeFile = RepoPaths.Resolve(settings.Docker.ComposeFile);
	private readonly string _repoRoot = RepoPaths.RepoRoot;
	private readonly DockerClient _client = new DockerClientConfiguration().CreateClient();
	private string? _containerId;
	private BasilHttpClientFactory? _clientFactory;

	public ServerHostCapabilities Capabilities { get; } = new(
		CanMeasureStartupTime: true,
		CanMeasureProcessMetrics: true, // CPU + working set only, via the Docker stats API
		CanMeasureDotnetCounters: false,
		CanSnapshotDatabase: true); // via the ./docker-data/Data bind mount

	public ServerEndpoint Endpoint { get; } = new(settings.Domain, settings.Port, IPAddress.Loopback);

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		await RunComposeAsync($"up -d {settings.Docker.ServiceName}", cancellationToken);
		_containerId = await ResolveContainerIdAsync(cancellationToken);
		_clientFactory = new BasilHttpClientFactory(Endpoint, new ClientSettings());
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		if (_containerId is not null)
		{
			try
			{
				await _client.Containers.StopContainerAsync(_containerId, new ContainerStopParameters(),
					cancellationToken);
			}
			catch (DockerApiException)
			{
				// The container is already gone; nothing to stop.
			}
			_containerId = null;
		}

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

		await using var sampler = new DockerStatsSampler(_client, _containerId);
		return await sampler.SampleAsync(cancellationToken);
	}

	public async Task ExportResultsAsync(string reportFolder, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(_containerId)) return;

		Directory.CreateDirectory(reportFolder);
		var logs = await ReadContainerLogsAsync(_containerId, cancellationToken);
		await File.WriteAllTextAsync(Path.Combine(reportFolder, "server-log-tail.txt"), logs, cancellationToken);
	}

	public Task<bool> SyncDatabaseSnapshotAsync(string snapshotPath, bool restoreIfPresent)
	{
		var bindMountRoot = RepoPaths.Resolve("docker-data");
		return Task.FromResult(ServerDatabase.SyncSnapshot(bindMountRoot, snapshotPath, restoreIfPresent));
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync();
		_clientFactory?.Dispose();
		_client.Dispose();
	}

	private async Task<string> ResolveContainerIdAsync(CancellationToken cancellationToken)
	{
		IList<ContainerListResponse> containers;
		try
		{
			containers = await _client.Containers.ListContainersAsync(new ContainersListParameters
			{
				All = true,
				Filters = new Dictionary<string, IDictionary<string, bool>>
				{
					["label"] = new Dictionary<string, bool>
						{ [$"{ComposeServiceLabel}={settings.Docker.ServiceName}"] = true },
					["status"] = new Dictionary<string, bool> { ["running"] = true }
				}
			}, cancellationToken);
		}
		catch (DockerApiException ex)
		{
			throw new InvalidOperationException(
				$"Failed to resolve the '{settings.Docker.ServiceName}' container id from the Docker daemon.", ex);
		}

		return containers.OrderByDescending(c => c.Created).FirstOrDefault()?.ID
		       ?? throw new InvalidOperationException(
			       $"No running container for compose service '{settings.Docker.ServiceName}' was found after " +
			       $"'docker compose up -d'. Is the service name correct in the profile?");
	}

	private async Task<string> ReadContainerLogsAsync(string containerId, CancellationToken cancellationToken)
	{
		try
		{
			using var stream = await _client.Containers.GetContainerLogsAsync(containerId, false,
				new ContainerLogsParameters
				{
					ShowStdout = true,
					ShowStderr = true,
					Tail = "500"
				}, cancellationToken);

			var builder = new StringBuilder();
			var buffer = new byte[8192];
			while (true)
			{
				var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
				if (result.EOF) break;
				builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
			}

			return builder.ToString();
		}
		catch (DockerApiException)
		{
			// The container is already gone; export what we can (i.e. nothing).
			return string.Empty;
		}
	}

	private Task RunComposeAsync(string arguments, CancellationToken cancellationToken)
	{
		return RunProcessAsync("docker", $"compose -f \"{_composeFile}\" {arguments}", _repoRoot, cancellationToken);
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
}
