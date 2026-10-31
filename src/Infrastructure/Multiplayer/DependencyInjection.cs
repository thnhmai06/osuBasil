using Basil.Infrastructure.Common.Persistence;
using Basil.Application.Multiplayer.Persistence;
using Basil.Infrastructure.Multiplayer.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Multiplayer;

/// <summary>Registers the persistence implementations owned by Multiplayer.</summary>
public static class DependencyInjection
{
	/// <summary>Adds Multiplayer persistence implementations.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddMultiplayerInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<MultiplayerDbContext>>();
		services.AddSingleton<IMatchRepository, PostgresMatchRepository>();
		services.AddSingleton<IMatchReportRepository, MatchReportRepository>();
		services.AddSingleton<IRoundRepository, PostgresRoundRepository>();
		services.AddSingleton<IRoundScoreRepository, PostgresRoundScoreRepository>();
		services.AddSingleton<IMatchEventRepository, PostgresMatchEventRepository>();
		return services;
	}
}