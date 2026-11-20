using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Scores.Workers;

/// <summary>At start-up, asks for the pp of the scores that still have none.</summary>
internal sealed class ScorePerformanceBackfill(
	IScoreRepository scores,
	IScorePerformanceRecalculator recalculator,
	ILogger<ScorePerformanceBackfill> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken ct)
	{
		try
		{
			if ((await scores.ListForPpAsync(0, true, 1, ct)).Count > 0)
				recalculator.TryStart(ScorePerformanceScope.Missing);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogError(exception, "Could not look for scores without performance points:");
		}
	}

	public Task StopAsync(CancellationToken ct)
	{
		return Task.CompletedTask;
	}
}