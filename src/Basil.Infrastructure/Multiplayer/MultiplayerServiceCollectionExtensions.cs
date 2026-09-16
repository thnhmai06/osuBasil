using Basil.Application.Multiplayer;
using Basil.Application.Shared.Configuration;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Multiplayer;

/// <summary>Registers the Multiplayer slice's services and endpoints.</summary>
public static class MultiplayerServiceCollectionExtensions
{
	/// <summary>Registers the Multiplayer slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddMultiplayer(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddMultiplayerApplication();

		services.AddSingleton<IMatchRepository>(sp =>
			new SqliteMatchRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteMatchRepository>>()));

		services.AddSingleton<MatchRoundEndOutbox>();
		services.AddSingleton<IMatchRoundEndOutbox>(sp => sp.GetRequiredService<MatchRoundEndOutbox>());
		services.AddHostedService(sp => sp.GetRequiredService<MatchRoundEndOutbox>());

		services.AddHostedService<MultiplayerMetricsPublisher>();

		return services;
	}

	/// <summary>Maps the Multiplayer slice's routes onto the `api.` host.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapMultiplayerRoutes(this RouteGroupBuilder group)
	{
		group.MapMatchRoutes();
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}