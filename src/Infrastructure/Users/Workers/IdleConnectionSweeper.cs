using Basil.Application.Users.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Users.Workers;

/// <summary>Closes idle connections and stores the activity of token sessions every 100 seconds.</summary>
internal sealed class IdleConnectionSweeper(
	UserRegistry registry,
	TimeProvider time,
	ILogger<IdleConnectionSweeper> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(100), time);
		while (await timer.WaitForNextTickAsync(stoppingToken))
			try
			{
				var closed = registry.CloseIdle();
				if (closed > 0) logger.LogDebug("Closed {ConnectionCount} idle connections", closed);
				await registry.FlushActivityAsync(stoppingToken);
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Could not close idle connections");
			}
	}
}