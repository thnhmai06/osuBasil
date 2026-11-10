using Basil.Application.Beatmaps.Services;
using Microsoft.Extensions.Options;
using Velopack;
using Velopack.Sources;

namespace Basil.Configuration;

/// <summary>
///     Looks for a newer release of Basil and of the osu! rulesets when the server starts and reports it in the log; it
///     never installs anything.
/// </summary>
internal sealed class UpdateChecker(
	IOptionsMonitor<UpdateOptions> options,
	IRulesetUpdater rulesets,
	ILogger<UpdateChecker> logger)
	: BackgroundService
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var update = options.CurrentValue;
		if (!update.CheckOnStartup)
		{
			logger.LogInformation("Skipped checking for updates because checking on startup is disabled");
			return;
		}

		await CheckAppAsync(update, stoppingToken);
		await CheckRulesetsAsync(stoppingToken);
	}

	private async Task CheckRulesetsAsync(CancellationToken stoppingToken)
	{
		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
			timeout.CancelAfter(Timeout);
			var check = await rulesets.CheckAsync(timeout.Token);
			if (check.UpdateAvailable)
				logger.LogWarning(
					"A new version of the osu! rulesets is available: {Version}. Run `ruleset update`, restart the server, then run `beatmapsets reload`",
					check.Latest);
			else logger.LogInformation("The osu! rulesets are up to date");
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !stoppingToken.IsCancellationRequested)
		{
			logger.LogWarning(exception, "Skipped checking for ruleset updates:");
		}
	}

	private async Task CheckAppAsync(UpdateOptions update, CancellationToken stoppingToken)
	{
		try
		{
			// A GitHub repository address is read as a list of releases; any other address is a plain feed folder.
			IUpdateSource source = Uri.TryCreate(update.Source, UriKind.Absolute, out var address) &&
			                       address.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
				? new GithubSource(update.Source, null, false)
				: new SimpleWebSource(update.Source);
			var manager = new UpdateManager(source, update.Channel == UpdateOptions.StableChannel
				? null
				: new Velopack.UpdateOptions { ExplicitChannel = update.Channel });
			if (!manager.IsInstalled)
			{
				logger.LogInformation("Skipped checking for updates because Basil was not installed by the updater");
				return;
			}

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
			timeout.CancelAfter(Timeout);
			var found = await manager.CheckForUpdatesAsync().WaitAsync(timeout.Token);
			if (found is null) logger.LogInformation("Basil is up to date");
			else logger.LogWarning("A new version of Basil is available: {Version}", found.TargetFullRelease.Version);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !stoppingToken.IsCancellationRequested)
		{
			logger.LogWarning(exception, "Skipped checking for updates because the release feed could not be reached:");
		}
	}
}