using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Persistence;
using Basil.Server.Shared.Storage;
using Microsoft.Extensions.Options;

namespace Basil.Server.Features.Scores;

/// <summary>Registers the Scores slice's services and endpoints.</summary>
public static class ScoresServiceCollectionExtensions
{
	/// <summary>Registers the Scores slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddScores(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<ScoreSubmissionService>();
		services.AddSingleton<ReplayService>();

		services.AddSingleton<IScoreRepository>(sp =>
			new SqliteScoreRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteScoreRepository>>()));
		services.AddSingleton<ILeaderboardStore>(sp => new SqliteLeaderboardStore(BuildConnectionString(sp)));

		services.AddSingleton<IScoreDecryptor, RijndaelScoreDecryptor>();
		services.AddSingleton<IReplayStorage, FileSystemReplayStorage>();

		return services;
	}

	/// <summary>Maps the Scores slice's routes onto the `api.` host.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapScoresRoutes(this RouteGroupBuilder group)
	{
		group.MapScoreRoutes();
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}