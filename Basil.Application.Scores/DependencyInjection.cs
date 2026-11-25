using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Scores.Logic;
using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Scores.Ports;
using Basil.Application.Scores.Services;
using Basil.Application.Scores.Persistence;
using Basil.Application.Shared.Events;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Persistence;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Scores;

public static class DependencyInjection
{
	public static IServiceCollection AddScoresApplication(this IServiceCollection services)
	{
		services.AddSingleton<ScorePerformanceRequests>(sp => new ScorePerformanceRequests(
			sp.GetRequiredService<ISender>(),
			sp.GetRequiredService<IBeatmapRepository>(),
			sp.GetRequiredService<IScoreRepository>(),
			sp.GetRequiredService<ISettingsRepository>(),
			sp.GetRequiredService<TimeProvider>()));
		services.AddSingleton<ScoreSubmitter>(sp => new ScoreSubmitter(
			sp.GetRequiredService<IScoreRecorder>(),
			sp.GetRequiredService<ScorePerformanceRequests>(),
			sp.GetRequiredService<IEventPublisher<ScoreEvent>>(),
			sp.GetRequiredService<IScoreBeatmapAcceptance>(),
			sp.GetRequiredService<TimeProvider>()));
		services.AddSingleton<ScorePerformances>(sp => new ScorePerformances(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IScorePerformanceRecalculator>()));
		services.AddSingleton<ScoreQueries>(sp => new ScoreQueries(
			sp.GetRequiredService<IBeatmapRepository>(),
			sp.GetRequiredService<IScoreRepository>(),
			sp.GetRequiredService<IUserRepository>()));
		return services;
	}
}