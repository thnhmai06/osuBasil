using Basil.Application.Multiplayer.Persistence;
using Basil.Infrastructure.Multiplayer.Persistence;
using Basil.Infrastructure.Multiplayer.Workers;
using Basil.Infrastructure.Shared.Persistence;
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
		services.AddSingleton<IMatchRepository, EfMatchRepository>();
		services.AddSingleton<IMatchReportRepository, MatchReportRepository>();
		services.AddSingleton<IRoundRepository, EfRoundRepository>();
		services.AddSingleton<IRoundScoreRepository, EfRoundScoreRepository>();
		services.AddSingleton<IMatchEventRepository, EfMatchEventRepository>();
		return services;
	}

	/// <summary>Adds the background work of Multiplayer: closing rooms that stay empty.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddMultiplayerBackgroundWork(this IServiceCollection services)
	{
		services.AddHostedService<EmptyRoomCloser>();
		return services;
	}
}