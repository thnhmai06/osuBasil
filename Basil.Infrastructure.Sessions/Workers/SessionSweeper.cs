using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Sessions.Workers;

internal sealed class SessionSweeper(
	SessionMaintenance maintenance,
	TimeProvider time,
	ILogger<SessionSweeper> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), time);
		while (await timer.WaitForNextTickAsync(stoppingToken))
			try
			{
				await maintenance.FlushActivityAsync();
				var ended = await maintenance.EndSilentAsync();
				if (ended > 0) logger.LogDebug("Ended {SessionCount} expired stored sessions", ended);
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Could not close expired sessions");
			}
	}
}