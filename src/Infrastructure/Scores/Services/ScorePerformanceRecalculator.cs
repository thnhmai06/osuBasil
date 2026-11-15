using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Services;
using Basil.Infrastructure.Scores.Logic;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Scores.Services;

/// <summary>Calculates the performance points of stored scores, one batch at a time in ascending order of id.</summary>
internal sealed class ScorePerformanceRecalculator(
	IScoreRepository scores,
	ScorePerformanceCalculator calculator,
	ILogger<ScorePerformanceRecalculator> logger) : IScorePerformanceRecalculator, IDisposable
{
	private const int BatchSize = 100;
	private const int ProgressEvery = 1000;

	private readonly CancellationTokenSource _stop = new();
	private int _running;

	/// <inheritdoc />
	public bool Running => Volatile.Read(ref _running) == 1;

	/// <inheritdoc />
	public bool TryStart(ScorePerformanceScope scope)
	{
		if (Interlocked.Exchange(ref _running, 1) == 1) return false;

		_ = Task.Run(() => RunAsync(scope, _stop.Token));
		return true;
	}

	/// <summary>Stops a running calculation when the server shuts down.</summary>
	public void Dispose()
	{
		_stop.Cancel();
		_stop.Dispose();
	}

	/// <summary>Runs the calculation and ends the running state; a failure is logged, never thrown.</summary>
	/// <param name="scope">Which scores it covers.</param>
	/// <param name="ct">A token that stops the run.</param>
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

	/// <summary>Walks the passed scores by id and calculates each of them.</summary>
	/// <returns>How many scores were given points and how many were left as they were.</returns>
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
