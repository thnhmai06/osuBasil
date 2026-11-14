using Basil.Application.Diagnostics.Services;
using Basil.Infrastructure.Diagnostics.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Diagnostics.Workers;

/// <summary>Takes the sample once a second from the start of the server and hands it to the feed.</summary>
internal sealed class DiagnosticsSampler(
	IDiagnosticsIntake intake,
	DiagnosticsFeed feed,
	TimeProvider clock,
	ILogger<DiagnosticsSampler> logger) : BackgroundService
{
	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
		try
		{
			do
			{
				try
				{
					feed.Publish(await intake.TakeSampleAsync(stoppingToken));
				}
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					logger.LogError(exception, "The diagnostics sample could not be taken");
				}
			} while (await timer.WaitForNextTickAsync(stoppingToken));
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// The server is stopping.
		}
	}
}