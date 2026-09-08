using Basil.Server.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Basil.Server.Host;

/// <summary>
///     Reports at startup whether a newer release of the server exists. It never installs one.
/// </summary>
/// <remarks>
///     The check runs in the background so that a slow or unreachable feed cannot delay the server
///     from accepting connections, and it gives up after <see cref="Timeout" />.
/// </remarks>
internal sealed class StartupUpdateCheck(
	IUpdateProbe probe,
	IOptions<UpdateCheckOptions> options,
	ILogger<StartupUpdateCheck> logger) : IHostedService
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

	private CancellationTokenSource? _cancellation;
	private Task? _check;

	/// <inheritdoc />
	public Task StartAsync(CancellationToken cancellationToken)
	{
		if (!options.Value.CheckOnStartup)
		{
			logger.LogWarning("Not checking for updates: the startup check is turned off in settings");
			return Task.CompletedTask;
		}

		_cancellation = new CancellationTokenSource(Timeout);
		_check = RunAsync(_cancellation.Token);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task StopAsync(CancellationToken cancellationToken)
	{
		if (_cancellation is null) return;

		await _cancellation.CancelAsync();
		if (_check is not null)
			try
			{
				await _check;
			}
			catch (OperationCanceledException)
			{
				// Shutting down before the check finished is not a failure worth reporting.
			}

		_cancellation.Dispose();
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		UpdateCheckResult result;
		try
		{
			result = await probe.CheckAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			logger.LogWarning("Stopped checking for updates: the release feed did not answer within {Timeout:0}s",
				Timeout.TotalSeconds);
			return;
		}
		catch (Exception exception)
		{
			// A check is a courtesy. Whatever went wrong looking for a newer release -- no network,
			// a refused connection, a malformed feed -- must not stop the server from serving.
			logger.LogWarning(exception, "Stopped checking for updates: the release feed could not be reached");
			return;
		}

		Report(result, logger);
	}

	/// <summary>Writes the one line a completed check produces.</summary>
	/// <param name="result">What the check found.</param>
	/// <param name="logger">The logger to write to.</param>
	public static void Report(UpdateCheckResult result, ILogger logger)
	{
		switch (result.Outcome)
		{
			case UpdateCheckOutcome.UpToDate:
				logger.LogInformation("Basil {Version} is up to date", BuildVersion.Informational);
				break;
			case UpdateCheckOutcome.UpdateAvailable:
				logger.LogWarning(
					"Basil {Available} is available; this server is running {Current}. Run the server with --update to install it",
					result.AvailableVersion, BuildVersion.Informational);
				break;
			case UpdateCheckOutcome.Failed:
				logger.LogWarning("Stopped checking for updates: the release feed could not be reached");
				break;
			case UpdateCheckOutcome.NotInstallable:
				logger.LogWarning("Not checking for updates: this copy of Basil was not installed by the updater");
				break;
		}
	}
}
