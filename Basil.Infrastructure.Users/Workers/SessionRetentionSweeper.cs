using Basil.Application.Users.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Users.Workers;

internal sealed class SessionRetentionSweeper(
	UserRegistry registry,
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
				var deleted = await registry.PurgeEndedSessionsAsync();
				if (deleted > 0) logger.LogInformation("Deleted {SessionCount} sessions past their retention", deleted);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				logger.LogError(exception, "Could not delete old sessions");
			}
		} while (await timer.WaitForNextTickAsync(stoppingToken));
	}
}