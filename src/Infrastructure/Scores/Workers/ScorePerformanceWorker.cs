using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Services;
using Basil.Infrastructure.Scores.Logic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Scores.Workers;

internal sealed class ScorePerformanceWorker(
	ScorePerformanceQueue queue,
	IScoreRepository scores,
	ScorePerformanceCalculator calculator,
	IScorePerformanceRecalculator recalculator,
	ILogger<ScorePerformanceWorker> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await StartMissingAsync(stoppingToken);
		var readers = Enumerable.Range(0, Math.Max(2, Environment.ProcessorCount))
			.Select(_ => Task.Run(() => DrainAsync(stoppingToken), CancellationToken.None));
		await Task.WhenAll(readers);
	}

	internal async Task StartMissingAsync(CancellationToken ct)
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

	internal async Task CalculateAsync(int id, CancellationToken ct)
	{
		try
		{
			if (await scores.GetAsync(id, ct) is { } score) await calculator.CalculateAsync(score, ct);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogError(exception, "Could not calculate the performance of score {Id}:", id);
		}
	}

	private async Task DrainAsync(CancellationToken ct)
	{
		try
		{
			await foreach (var id in queue.Reader.ReadAllAsync(ct)) await CalculateAsync(id, ct);
		}
		catch (OperationCanceledException)
		{
		}
	}
}
