using Basil.Application.Rulesets.Services;
using Basil.Application.Servers.Ports;
using Basil.Application.Servers.Services;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Servers.Objects;

/// <summary>Keeps what is known about newer releases of Basil and of the osu! calculation stack.</summary>
public sealed class UpdateStatus(
	IReleases releases,
	IRulesetUpdater rulesets,
	ILogger<UpdateStatus> logger,
	TimeProvider clock) : IUpdateStatus
{
	private readonly Lock _gate = new();
	private DateTimeOffset? _lastCheck;
	private UpdateReport _status = new(UpdateState.NotChecked, null, UpdateState.NotChecked, null);

	public UpdateReport Status
	{
		get
		{
			lock (_gate)
			{
				return _status;
			}
		}
	}

	public DateTimeOffset? LastCheck
	{
		get
		{
			lock (_gate)
			{
				return _lastCheck;
			}
		}
	}

	public async Task CheckNowAsync(CancellationToken ct = default)
	{
		await CheckAppAsync(ct);
		CheckRulesets();
		lock (_gate)
		{
			_lastCheck = clock.GetUtcNow();
		}
	}

	/// <summary>Records that checking was switched off, so the report says so instead of "not checked".</summary>
	public void Skip()
	{
		Set(_ => new UpdateReport(UpdateState.Skipped, null, UpdateState.Skipped, null));
	}

	private void Set(Func<UpdateReport, UpdateReport> change)
	{
		lock (_gate)
		{
			_status = change(_status);
		}
	}

	private void CheckRulesets()
	{
		var stack = rulesets.Version();
		var installed = stack.State == RulesetState.Installed;
		Set(status => status with
		{
			Rulesets = installed ? UpdateState.UpToDate : UpdateState.Available,
			RulesetsVersion = installed ? null : stack.Version
		});
		if (!installed)
			logger.LogWarning("The osu! calculation stack {Version} is {State}. Run `ruleset update` to install it",
				stack.Version, stack.State);
	}

	private async Task CheckAppAsync(CancellationToken stoppingToken)
	{
		try
		{
			var latest = await releases.LatestAsync(stoppingToken);
			var available = Version.TryParse(latest.TrimStart('v'), out var tag) &&
			                Version.TryParse(releases.Installed, out var current) &&
			                Normalize(tag) > Normalize(current);
			Set(status => status with
			{
				Basil = available ? UpdateState.Available : UpdateState.UpToDate,
				BasilVersion = available ? latest.TrimStart('v') : null
			});
			if (available) logger.LogWarning("A new version of Basil is available: {Version}", latest);
			else logger.LogInformation("Basil is up to date");
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !stoppingToken.IsCancellationRequested)
		{
			Set(status => status with { Basil = UpdateState.Failed, BasilVersion = null });
			logger.LogWarning(exception, "Skipped checking for updates because the release feed could not be reached:");
		}
	}

	// A four-part assembly version and a three-part release tag compare equal when they name the same release.
	private static Version Normalize(Version version)
	{
		return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
	}
}
