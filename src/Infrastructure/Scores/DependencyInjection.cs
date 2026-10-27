using Basil.Infrastructure.Common.Postgres;
using Basil.Application.Scores.Persistence;
using Basil.Infrastructure.Scores.Persistence;
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
		services.AddSingleton<IScoreRepository, PostgresScoreRepository>();
		services.AddSingleton<IUserStatsRepository, PostgresUserStatsRepository>();
		services.AddSingleton<IReplayStorage, FileReplayStorage>();
		return services;
	}
}