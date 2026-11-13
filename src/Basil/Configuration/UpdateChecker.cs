using Basil.Application.Beatmaps.Services;
using Basil.Host.Shared.Info;
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
	ILogger<UpdateChecker> logger,
	TimeProvider clock)
	: BackgroundService, IUpdateStatus
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
	private readonly Lock _gate = new();
	private DateTimeOffset? _lastCheck;
	private UpdateReport _status = new(UpdateState.NotChecked, null, UpdateState.NotChecked, null);

	/// <inheritdoc />
	public UpdateReport Status
	{
		get
		{
			lock (_gate) return _status;
		}
	}

	/// <inheritdoc />
	public DateTimeOffset? LastCheck
	{
		get
		{
			lock (_gate) return _lastCheck;
		}
	}

	/// <inheritdoc />
	public async Task CheckNowAsync(CancellationToken ct = default)
	{
		await CheckAppAsync(options.CurrentValue, ct);
		await CheckRulesetsAsync(ct);
		lock (_gate) _lastCheck = clock.GetUtcNow();
	}

	private void Set(Func<UpdateReport, UpdateReport> change)
	{
		lock (_gate) _status = change(_status);
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var update = options.CurrentValue;
		if (!update.CheckOnStartup)
		{
			logger.LogInformation("Skipped checking for updates because checking on startup is disabled");
			Set(_ => new UpdateReport(UpdateState.Skipped, null, UpdateState.Skipped, null));
			return;
		}

		await CheckNowAsync(stoppingToken);
	}

	private async Task CheckRulesetsAsync(CancellationToken stoppingToken)
	{
		try
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
			timeout.CancelAfter(Timeout);
			var check = await rulesets.CheckAsync(timeout.Token);
			Set(status => status with
			{
				Rulesets = check.UpdateAvailable ? UpdateState.Available : UpdateState.UpToDate,
				RulesetsVersion = check.UpdateAvailable ? check.Latest : null
			});
			if (check.UpdateAvailable)
				logger.LogWarning(
					"A new version of the osu! rulesets is available: {Version}. Run `ruleset update`, restart the server, then run `beatmapsets reload`",
					check.Latest);
			else logger.LogInformation("The osu! rulesets are up to date");
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !stoppingToken.IsCancellationRequested)
		{
			Set(status => status with { Rulesets = UpdateState.Failed, RulesetsVersion = null });
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
				Set(status => status with { Basil = UpdateState.Skipped, BasilVersion = null });
				return;
			}

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
			timeout.CancelAfter(Timeout);
			var found = await manager.CheckForUpdatesAsync().WaitAsync(timeout.Token);
			Set(status => status with
			{
				Basil = found is null ? UpdateState.UpToDate : UpdateState.Available,
				BasilVersion = found?.TargetFullRelease.Version.ToString()
			});
			if (found is null) logger.LogInformation("Basil is up to date");
			else logger.LogWarning("A new version of Basil is available: {Version}", found.TargetFullRelease.Version);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !stoppingToken.IsCancellationRequested)
		{
			Set(status => status with { Basil = UpdateState.Failed, BasilVersion = null });
			logger.LogWarning(exception, "Skipped checking for updates because the release feed could not be reached:");
		}
	}
}