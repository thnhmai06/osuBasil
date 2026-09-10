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
	/// <remarks>Stopping a second time does nothing rather than failing.</remarks>
	public async Task StopAsync(CancellationToken cancellationToken)
	{
		// Taken and cleared together, so a second stop finds nothing to do. A host can stop a
		// service more than once -- shutting down and then disposing is the ordinary case -- and a
		// cancellation source that has already been disposed throws when it is cancelled again.
		var cancellation = _cancellation;
		var check = _check;
		_cancellation = null;
		_check = null;

		if (cancellation is null) return;

		await cancellation.CancelAsync();
		if (check is not null)
			try
			{
				await check;
			}
			catch (OperationCanceledException)
			{
				// Shutting down before the check finished is not a failure worth reporting.
			}

		cancellation.Dispose();
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
