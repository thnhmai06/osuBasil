using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Sessions.Workers;

internal sealed class SessionSweeper(
	SessionRegistry registry,
	TimeProvider time,
	ILogger<SessionSweeper> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(100), time);
		while (await timer.WaitForNextTickAsync(stoppingToken))
			try
			{
				await registry.FlushActivityAsync();
				var ended = await registry.EndSilentAsync();
				if (ended > 0) logger.LogDebug("Ended {SessionCount} expired stored sessions", ended);
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Could not close expired sessions");
			}
	}
}