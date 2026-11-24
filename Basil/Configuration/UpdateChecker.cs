using Basil.Application.Rulesets.Services;
using Basil.Host.Shared.Info;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Basil.Configuration;

internal sealed class UpdateChecker(
	IOptionsMonitor<UpdateOptions> options,
	IRulesetUpdater rulesets,
	IHttpClientFactory http,
	ILogger<UpdateChecker> logger,
	TimeProvider clock)
	: BackgroundService, IUpdateStatus
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
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
		await CheckAppAsync(options.CurrentValue, ct);
		await CheckRulesetsAsync(ct);
		lock (_gate)
		{
			_lastCheck = clock.GetUtcNow();
		}
	}

	private void Set(Func<UpdateReport, UpdateReport> change)
	{
		lock (_gate)
		{
			_status = change(_status);
		}
	}

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
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
			timeout.CancelAfter(Timeout);
			var latest = await LatestReleaseAsync(update.Source, timeout.Token);
			var available = Version.TryParse(latest.TrimStart('v'), out var tag) &&
			                Version.TryParse(AppVersion.Current, out var current) &&
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

	private async Task<string> LatestReleaseAsync(string repository, CancellationToken ct)
	{
		var path = new Uri(repository).AbsolutePath.Trim('/');
		using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{path}/releases/latest");
		request.Headers.UserAgent.ParseAdd("Basil-update-check");
		request.Headers.Accept.ParseAdd("application/vnd.github+json");
		using var response = await http.CreateClient().SendAsync(request, ct);
		response.EnsureSuccessStatusCode();
		using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
		return body.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
	}

	// A four-part assembly version and a three-part release tag compare equal when they name the same release.
	private static Version Normalize(Version version)
	{
		return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
	}
}