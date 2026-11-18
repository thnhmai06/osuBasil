using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Services;
using Basil.Infrastructure.Scores.Logic;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Scores.Services;

internal sealed class ScorePerformanceRecalculator(
	IScoreRepository scores,
	ScorePerformanceCalculator calculator,
	ILogger<ScorePerformanceRecalculator> logger) : IScorePerformanceRecalculator, IDisposable
{
	private const int BatchSize = 100;
	private const int ProgressEvery = 1000;

	private readonly CancellationTokenSource _stop = new();
	private int _running;

	public bool Running => Volatile.Read(ref _running) == 1;

	public bool TryStart(ScorePerformanceScope scope)
	{
		if (Interlocked.Exchange(ref _running, 1) == 1) return false;

		_ = Task.Run(() => RunAsync(scope, _stop.Token));
		return true;
	}

	public void Dispose()
	{
		_stop.Cancel();
		_stop.Dispose();
	}

	internal async Task RunAsync(ScorePerformanceScope scope, CancellationToken ct)
	{
		try
		{
			var (done, skipped) = await CalculateAllAsync(scope, ct);
			logger.LogInformation("Calculated the performance of {Done} scores ({Skipped} kept what they have)", done,
				skipped);
		}
		catch (OperationCanceledException)
		{
			logger.LogInformation("Calculating the performance of scores was stopped");
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Calculating the performance of scores failed:");
		}
		finally
		{
			Volatile.Write(ref _running, 0);
		}
	}

	internal async Task<(int Done, int Skipped)> CalculateAllAsync(ScorePerformanceScope scope, CancellationToken ct)
	{
		int done = 0, skipped = 0, afterId = 0;
		while (true)
		{
			var batch = await scores.ListForPpAsync(afterId, scope == ScorePerformanceScope.Missing, BatchSize, ct);
			if (batch.Count == 0) return (done, skipped);

			foreach (var score in batch)
			{
				if (await calculator.CalculateAsync(score, ct)) done++;
				else skipped++;

				if ((done + skipped) % ProgressEvery == 0)
					logger.LogInformation("Calculating the performance of scores: {Count} done", done + skipped);
			}

			afterId = batch[^1].Id;
		}
	}
}
