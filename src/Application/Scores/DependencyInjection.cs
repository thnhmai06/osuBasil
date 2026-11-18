using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Scores.Ports;
using Basil.Application.Scores.Services;
using Basil.Application.Shared.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Scores;

public static class DependencyInjection
{
	public static IServiceCollection AddScoresApplication(this IServiceCollection services)
	{
		services.AddSingleton<ScoreSubmitter>(sp => new ScoreSubmitter(
			sp.GetRequiredService<IScoreRecorder>(),
			sp.GetRequiredService<IEventPublisher<ScoreEvent>>(),
			sp.GetRequiredService<IScoreBeatmapAcceptance>(),
			sp.GetRequiredService<TimeProvider>()));
		services.AddSingleton<ScorePerformances>(sp => new ScorePerformances(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IScorePerformanceRecalculator>()));
		return services;
	}
}