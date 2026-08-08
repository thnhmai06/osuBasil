using System.Diagnostics;
using System.Net;
using Basil.LoadTests.Client;
using Basil.LoadTests.Configuration;
using Basil.LoadTests.Helpers;
using Basil.LoadTests.Infrastructure.Metrics;

namespace Basil.LoadTests.Hosting;

/// <summary>
///     Runs Basil.Web as a local child process — either a pre-published binary (the default, and the
///     only mode with trustworthy process metrics: a published exe has no wrapper process) or
///     <c>dotnet run</c> (faster to iterate, but process metrics would measure the wrong process, so
///     they are reported as unavailable in that mode).
/// </summary>
public sealed class DotnetServerHost : IServerHost
{
	private readonly ServerHostSettings _settings;
	private readonly Action<string> _logWarning;
	private readonly string _serverDirectory;
	private readonly string _countersCsvPath;
	private readonly bool _processMetricsTrustworthy;

	private Process? _process;
	private ProcessResourceSampler? _processSampler;
	private DotnetCountersSidecar? _countersSampler;
	private BasilHttpClientFactory? _clientFactory;

	public DotnetServerHost(ServerHostSettings settings, Action<string> logWarning)
	{
		_settings = settings;
		_logWarning = logWarning;
		_processMetricsTrustworthy = settings.Dotnet.Mode == DotnetLaunchMode.Published;
		_serverDirectory = settings.Dotnet.Mode == DotnetLaunchMode.Published
			? RepoPaths.Resolve(settings.Dotnet.PublishDirectory)
			: RepoPaths.Resolve("src/Basil.Web");
		_countersCsvPath = Path.Combine(_serverDirectory, "dotnet-counters.csv");

		Endpoint = new ServerEndpoint(settings.Domain, settings.Port, IPAddress.Loopback);
		Capabilities = new ServerHostCapabilities(
			CanMeasureStartupTime: true,
			CanMeasureProcessMetrics: _processMetricsTrustworthy,
			CanMeasureDotnetCounters: _processMetricsTrustworthy,
			CanSnapshotDatabase: true);
	}

	public ServerHostCapabilities Capabilities { get; }
	public ServerEndpoint Endpoint { get; }

	public async Task StartAsync(CancellationToken cancellationToken = default)
	{
		if (_settings.Dotnet.Mode == DotnetLaunchMode.Published)
			await EnsurePublishedAsync(cancellationToken);

		var certPath = RepoPaths.Resolve(_settings.CertPath);
		var arguments = _settings.Dotnet.Mode == DotnetLaunchMode.Published
			? BuildServerArguments(certPath)
			: $"run --project \"{_serverDirectory}\" -c Release -- {BuildServerArguments(certPath)}";

		var fileName = _settings.Dotnet.Mode == DotnetLaunchMode.Published
			? Path.Combine(_serverDirectory, OperatingSystem.IsWindows() ? "Basil.Web.exe" : "Basil.Web")
			: "dotnet";

		var startInfo = new ProcessStartInfo(fileName, arguments)
		{
			WorkingDirectory = _serverDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		_process = Process.Start(startInfo) ??
		           throw new InvalidOperationException($"Failed to start server process '{fileName}'.");

		// The server's stdout/stderr are redirected, but nobody consumes them; a redirected pipe that
		// is never drained blocks the child as soon as its buffer fills. Drain both to a discard task.
		_ = _process.StandardOutput.ReadToEndAsync(cancellationToken);
		_ = _process.StandardError.ReadToEndAsync(cancellationToken);

		_clientFactory = new BasilHttpClientFactory(Endpoint, new ClientSettings());

		if (_processMetricsTrustworthy)
		{
			_processSampler = new ProcessResourceSampler(_process.Id, _settings.Port);
			_countersSampler = new DotnetCountersSidecar(_process.Id, _countersCsvPath, 1, _logWarning);
			await _countersSampler.StartAsync(cancellationToken);
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		if (_countersSampler is not null) await _countersSampler.StopAsync(cancellationToken);

		if (_process is { HasExited: false })
		{
			try
			{
				_process.CloseMainWindow();
				using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
				graceCts.CancelAfter(TimeSpan.FromSeconds(5));
				await _process.WaitForExitAsync(graceCts.Token);
			}
			catch (OperationCanceledException)
			{
				_process.Kill(true);
				await _process.WaitForExitAsync(cancellationToken);
			}
		}

		_clientFactory?.Dispose();
		_clientFactory = null;
	}

	public async Task<TimeSpan?> WaitUntilHealthyAsync(CancellationToken cancellationToken = default)
	{
		if (_clientFactory is null)
			throw new InvalidOperationException($"{nameof(StartAsync)} must complete before waiting for health.");

		return await ServerReadinessProbe.WaitAsync(_clientFactory, _settings.StartupTimeout, cancellationToken);
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

	public async Task ExportResultsAsync(string reportFolder, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(reportFolder);

		if (File.Exists(_countersCsvPath))
			File.Copy(_countersCsvPath, Path.Combine(reportFolder, "dotnet-counters.csv"), true);

		var logPath = Path.Combine(_serverDirectory, "Logs", "latest.log");
		if (File.Exists(logPath))
		{
			// The just-stopped server process can still hold the file open for a moment on Windows;
			// a locked log must never abort report writing, so retry briefly then skip with a warning.
			var tail = await TryReadLogTailAsync(logPath, cancellationToken);
			if (tail is not null)
			{
				await File.WriteAllLinesAsync(Path.Combine(reportFolder, "server-log-tail.txt"),
					tail.TakeLast(500), cancellationToken);
			}
		}
	}

	/// <summary>Reads a log file with a short retry, returning <see langword="null" /> if it stays locked.</summary>
	private async Task<string[]?> TryReadLogTailAsync(string path, CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < 3; attempt++)
		{
			try
			{
				return await File.ReadAllLinesAsync(path, cancellationToken);
			}
			catch (IOException) when (attempt < 2)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
			}
			catch (IOException)
			{
				_logWarning($"Server log '{path}' is locked; its tail will not be included in the report.");
				return null;
			}
		}

		return null;
	}

	public Task<bool> SyncDatabaseSnapshotAsync(string snapshotPath, bool restoreIfPresent,
		CancellationToken cancellationToken = default)
	{
		if (_process is { HasExited: false })
			throw new InvalidOperationException("The server must be stopped before syncing its database snapshot.");

		return Task.FromResult(ServerDatabase.SyncSnapshot(_serverDirectory, snapshotPath, restoreIfPresent));
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync();
		if (_countersSampler is not null) await _countersSampler.DisposeAsync();
		if (_processSampler is not null) await _processSampler.DisposeAsync();
		_process?.Dispose();
	}

	private string BuildServerArguments(string certPath)
	{
		return string.Join(' ',
			$"--Basil:Server:Domain={_settings.Domain}",
			$"--Basil:Server:Port={_settings.Port}",
			$"--Basil:Server:CertPath={certPath}",
			$"--Basil:Server:CertPassword={_settings.CertPassword}",
			$"--Basil:Irc:Port={_settings.IrcPort}",
			"--Basil:Logging:MinimumLevel=Information");
	}

	private async Task EnsurePublishedAsync(CancellationToken cancellationToken)
	{
		var executablePath = Path.Combine(_serverDirectory, OperatingSystem.IsWindows() ? "Basil.Web.exe" : "Basil.Web");
		if (File.Exists(executablePath))
		{
			if (!_settings.Dotnet.AutoPublish) return;
			// A stale publish from a previous checkout is fine to reuse — republishing on every
			// run would defeat the point of the snapshot/publish-once model. Delete the directory
			// manually to force a fresh publish.
			return;
		}

		var webProject = RepoPaths.Resolve("src/Basil.Web");
		var startInfo = new ProcessStartInfo("dotnet",
			$"publish \"{webProject}\" -c Release -o \"{_serverDirectory}\"")
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		using var publish = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet publish.");
		var output = await publish.StandardOutput.ReadToEndAsync(cancellationToken);
		var error = await publish.StandardError.ReadToEndAsync(cancellationToken);
		await publish.WaitForExitAsync(cancellationToken);

		if (publish.ExitCode != 0)
			throw new InvalidOperationException($"dotnet publish failed (exit {publish.ExitCode}):\n{output}\n{error}");
	}
}
