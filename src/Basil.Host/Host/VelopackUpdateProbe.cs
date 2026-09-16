using Basil.Server.Shared.Configuration;
using Velopack;
using Velopack.Sources;

namespace Basil.Server.Host;

/// <summary>Looks up releases through the updater that packaged this server.</summary>
internal sealed class VelopackUpdateProbe(UpdateCheckOptions options) : IUpdateProbe
{
	private readonly UpdateManager _manager = new(BuildSource(options.Source));

	/// <inheritdoc />
	public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? BuildVersion.Informational;

	/// <inheritdoc />
	public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
	{
		if (!_manager.IsInstalled) return new UpdateCheckResult(UpdateCheckOutcome.NotInstallable);

		try
		{
			var update = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
			return update is null
				? new UpdateCheckResult(UpdateCheckOutcome.UpToDate)
				: new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable,
					update.TargetFullRelease.Version.ToString());
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return new UpdateCheckResult(UpdateCheckOutcome.Failed);
		}
	}

	/// <inheritdoc />
	public async Task<bool> ApplyAsync(CancellationToken cancellationToken)
	{
		if (!_manager.IsInstalled) return false;

		var update = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
		if (update is null) return false;

		await _manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
		_manager.ApplyUpdatesAndRestart(update);
		return true;
	}

	private static IUpdateSource BuildSource(string source)
	{
		return source.Contains("github.com", StringComparison.OrdinalIgnoreCase)
			? new GithubSource(source, null, false)
			: new SimpleWebSource(source);
	}
}
