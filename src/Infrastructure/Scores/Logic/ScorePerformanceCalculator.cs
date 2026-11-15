using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Settings;
using Basil.Domain.Scores;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Scores.Logic;

/// <summary>Calculates the performance points of a stored score and stores them, within the time the settings allow.</summary>
internal sealed class ScorePerformanceCalculator(
	IBeatmapRepository beatmaps,
	IBeatmapCalculator calculator,
	IScoreRepository scores,
	ISettingsRepository settings,
	TimeProvider clock,
	ILogger<ScorePerformanceCalculator> logger)
{
	/// <summary>Calculates and stores the performance points of a score.</summary>
	/// <param name="score">The stored score.</param>
	/// <param name="ct">A token that stops the calculation when the server shuts down.</param>
	/// <returns>
	///     <see langword="true" /> when the points were stored; <see langword="false" /> when the score keeps what it
	///     has: it was failed, its beatmap or difficulty file is missing, or the calculation failed or ran out of time.
	/// </returns>
	public async Task<bool> CalculateAsync(ScoreRecord score, CancellationToken ct)
	{
		var data = score.Value;
		if (!data.IsPassed || data.BeatmapHash is not { } hash) return false;
		if (await beatmaps.GetAsync(hash, ct) is not { } beatmap) return false;

		var limit = (await settings.GetAsync(ct)).Beatmaps.PerformanceTimeout;
		using var timeout = new CancellationTokenSource(limit, clock);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
		BeatmapPerformance performance;
		try
		{
			performance = await calculator.GetPerformanceAsync(beatmap, data.Mode, data.Mods, data.LastState.HitCounts,
				data.LastState.MaxCombo, linked.Token);
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			logger.LogWarning("The performance of score {Id} was not ready within {Limit}", score.Id, limit);
			return false;
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogWarning(exception, "The performance of score {Id} could not be calculated:", score.Id);
			return false;
		}

		await scores.SetPpAsync(score.Id, performance.Pp);
		return true;
	}
}
