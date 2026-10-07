using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Storage.Caching;

/// <summary>Commits pending storage changes whenever a batch is due, and once more after shutdown.</summary>
internal sealed class WriteFlusher(WriteBuffer buffer, ILogger<WriteFlusher> logger)
	: BackgroundService, IHostedLifecycleService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			while (true)
			{
				await buffer.WaitUntilDueAsync(stoppingToken);
				try
				{
					await buffer.FlushAsync(stoppingToken);
				}
				catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
				{
					logger.LogError(exception, "Failed to flush pending storage changes");
				}
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// Shutdown stops the loop; StoppedAsync performs the final flush.
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
			logger.LogError(exception, "Failed to flush storage changes after shutdown");
		}
	}
}