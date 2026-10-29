using Basil.Application.Users.Users;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Users.Runtime;

/// <summary>Deletes the sessions that ended longer ago than the server settings keep them, once an hour.</summary>
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
				var deleted = await registry.PurgeEndedSessionsAsync(stoppingToken);
				if (deleted > 0) logger.LogInformation("Deleted {SessionCount} sessions past their retention", deleted);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				logger.LogError(exception, "Could not delete old sessions");
			}
		} while (await timer.WaitForNextTickAsync(stoppingToken));
	}
}
