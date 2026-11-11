using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Ports;
using Basil.Application.Scores.Storage;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Scores.Logic;
using Basil.Infrastructure.Scores.Persistence;
using Basil.Infrastructure.Scores.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Scores;

/// <summary>Registers the persistence implementations owned by Scores.</summary>
public static class DependencyInjection
{
	/// <summary>Adds Scores persistence implementations.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddScoresInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<ScoresDbContext>>();
		services.AddSingleton<IScoreRepository, EfScoreRepository>();
		services.AddSingleton<IUserStatsRepository, EfUserStatsRepository>();
		services.AddSingleton<IReplayStorage, FileReplayStorage>();
		services.AddScoresLogic();
		return services;
	}

	/// <summary>Adds the rules that validate and record a submitted score.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddScoresLogic(this IServiceCollection services)
	{
		services.AddSingleton<IScoreRecorder, ScoreRecorder>();
		return services;
	}
}