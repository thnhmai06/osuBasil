using Basil.Application.Rulesets.Messages;
using Basil.Application.Rulesets.Objects;
using Basil.Application.Rulesets.Ports;
using Basil.Application.Shared.Settings;
using Basil.Domain.Game;
using Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Rulesets.Workers;

/// <summary>Works through the queued calculations; every request ends in one result, a null one when it failed or ran out of time.</summary>
internal sealed class CalculationWorker(
	CalculationQueue queue,
	IBeatmapCalculator calculator,
	IBeatmapAnalyser analyser,
	IBeatmapFiles files,
	RulesetLibrary library,
	ISettingsRepository settings,
	TimeProvider clock,
	ILogger<CalculationWorker> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var readers = Enumerable.Range(0, Math.Max(2, Environment.ProcessorCount))
			.Select(_ => Task.Run(() => DrainAsync(stoppingToken), CancellationToken.None));
		await Task.WhenAll(readers);
	}

	internal async Task HandleAsync(IBaseRequest request, CancellationToken ct)
	{
		switch (request)
		{
			case CalculateBeatmap beatmap:
				library.BeatmapCalculated(beatmap.BeatmapId,
					await TryAsync($"beatmap {beatmap.BeatmapId}", token => AnalyseAsync(beatmap, token), ct));
				break;
			case CalculateScorePerformance score:
				library.ScorePerformanceCalculated(score.Key,
					await TryAsync($"a score on beatmap {score.BeatmapId}", async token =>
						await calculator.GetPerformanceAsync(score.BeatmapId, score.Checksum, score.Mode, score.Mods,
							score.Hits, score.MaxCombo, token), ct));
				break;
		}
	}

	private async Task<BeatmapAnalysis?> AnalyseAsync(CalculateBeatmap request, CancellationToken ct)
	{
		var content = await files.OpenAsync(request.BeatmapId, request.Checksum, ct) ??
		              throw new InvalidOperationException("The server has no file for the beatmap.");
		await using (content)
		{
			return analyser.Analyze(content, request.Mode, GameMods.NoMod);
		}
	}

	private async Task<T?> TryAsync<T>(string what, Func<CancellationToken, Task<T>> work, CancellationToken ct)
	{
		try
		{
			var limit = (await settings.GetAsync(ct)).Beatmaps.PerformanceTimeout;
			using var timeout = new CancellationTokenSource(limit, clock);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
			return await work(linked.Token);
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			logger.LogWarning("The calculation of {What} was not ready in time", what);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogWarning(exception, "The calculation of {What} failed:", what);
		}

		return default;
	}

	private async Task DrainAsync(CancellationToken ct)
	{
		try
		{
			await foreach (var request in queue.Reader.ReadAllAsync(ct)) await HandleAsync(request, ct);
		}
		catch (OperationCanceledException)
		{
		}
	}
}