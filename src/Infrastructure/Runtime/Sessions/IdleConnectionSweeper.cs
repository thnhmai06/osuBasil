using Basil.Application.Services.Contracts.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Sessions;

internal sealed class IdleConnectionSweeper(
	ISessionService sessions,
	TimeProvider time,
	ILogger<IdleConnectionSweeper> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(100), time);
		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			var closed = sessions.CloseIdle();
			if (closed > 0) logger.LogDebug("Closed {ConnectionCount} idle connections.", closed);
		}
	}
}
