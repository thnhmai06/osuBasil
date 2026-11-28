using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Sessions.Workers;

internal sealed class SessionRetentionSweeper(
	SessionMaintenance maintenance,
	TimeProvider time,
	ILogger<SessionRetentionSweeper> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
		do
		{
			try
			{
				var deleted = await maintenance.PurgeEndedSessionsAsync();
				if (deleted > 0) logger.LogInformation("Deleted {SessionCount} sessions past their retention", deleted);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				logger.LogError(exception, "Could not delete old sessions");
			}
		} while (await timer.WaitForNextTickAsync(stoppingToken));
	}
}