using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Storage.Caching;

/// <summary>Periodically commits pending storage changes and flushes them after shutdown.</summary>
internal sealed class WriteFlusher(WriteBuffer buffer, TimeProvider timeProvider, ILogger<WriteFlusher> logger)
	: BackgroundService, IHostedLifecycleService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), timeProvider);
		try
		{
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				try
				{
					await buffer.FlushAsync(stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception exception)
				{
					logger.LogError(exception, "Failed to flush pending storage changes.");
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Shutdown stops the timer loop; StoppedAsync performs the final flush.
		}
	}

	public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async Task StoppedAsync(CancellationToken cancellationToken)
	{
		try
		{
			await buffer.FlushAsync(cancellationToken);
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Failed to flush storage changes after shutdown.");
		}
	}
}
