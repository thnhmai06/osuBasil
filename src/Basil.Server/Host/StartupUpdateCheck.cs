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
			logger.LogWarning("Skipped checking for updates because checking on startup is disabled.");
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
			logger.LogWarning("Skipped checking for updates because the release feed could not be reached.");
			return;
		}
		catch (Exception exception)
		{
			// A check is a courtesy. Whatever went wrong looking for a newer release -- no network,
			// a refused connection, a malformed feed -- must not stop the server from serving.
			logger.LogWarning(exception, "Skipped checking for updates because the release feed could not be reached.");
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
				logger.LogInformation("Basil is up to date.");
				break;
			case UpdateCheckOutcome.UpdateAvailable:
				logger.LogWarning(
					"A new version of Basil is available. Terminate the server and restart it with --update to update.");
				break;
			case UpdateCheckOutcome.Failed:
				logger.LogWarning("Skipped checking for updates because the release feed could not be reached.");
				break;
			case UpdateCheckOutcome.NotInstallable:
				logger.LogWarning("Skipped checking for updates because Basil was not installed by the updater.");
				break;
		}
	}
}
