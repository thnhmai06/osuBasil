using Basil.Application.Multiplayer;
using Basil.Application.Shared.Configuration;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Multiplayer;

/// <summary>Registers the Multiplayer slice's services.</summary>
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

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}