using Basil.Application.Multiplayer.Persistence;
using Basil.Infrastructure.Multiplayer.Persistence;
using Basil.Infrastructure.Multiplayer.Workers;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Multiplayer;

public static class DependencyInjection
{
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

	public static IServiceCollection AddMultiplayerBackgroundWork(this IServiceCollection services)
	{
		services.AddHostedService<EmptyRoomCloser>();
		return services;
	}
}