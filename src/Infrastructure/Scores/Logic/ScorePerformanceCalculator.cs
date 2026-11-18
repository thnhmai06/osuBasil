using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Settings;
using Basil.Domain.Game.Scores;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Scores.Logic;

internal sealed class ScorePerformanceCalculator(
	IBeatmapRepository beatmaps,
	IBeatmapCalculator calculator,
	IScoreRepository scores,
	ISettingsRepository settings,
	TimeProvider clock,
	ILogger<ScorePerformanceCalculator> logger)
{
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
