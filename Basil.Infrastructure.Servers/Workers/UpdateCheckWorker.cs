using Basil.Application.Servers.Objects;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Servers.Workers;

internal sealed class UpdateCheckWorker(
	IOptionsMonitor<UpdateOptions> options,
	UpdateStatus status,
	ILogger<UpdateCheckWorker> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (!options.CurrentValue.CheckOnStartup)
		{
			logger.LogInformation("Skipped checking for updates because checking on startup is disabled");
			status.Skip();
			return;
		}

		try
		{
			await status.CheckNowAsync(stoppingToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
		                                  !stoppingToken.IsCancellationRequested)
		{
			logger.LogWarning(exception, "Checking for updates failed and is turned off");
		}
	}
}