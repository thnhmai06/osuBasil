using Basil.Application.Shared.Configuration;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Shared.Storage;
using Basil.Application.Scores;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Scores;

/// <summary>Registers the Scores slice's services.</summary>
public static class ScoresServiceCollectionExtensions
{
	/// <summary>Registers the Scores slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddScores(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddScoresApplication();
		services.AddSingleton<ReplayService>();

		services.AddSingleton<IScoreRepository>(sp =>
			new SqliteScoreRepository(BuildConnectionString(sp),
				sp.GetRequiredService<ILogger<SqliteScoreRepository>>()));
		services.AddSingleton<ILeaderboardStore>(sp => new SqliteLeaderboardStore(BuildConnectionString(sp)));

		services.AddSingleton<IScoreDecryptor, RijndaelScoreDecryptor>();
		services.AddSingleton<IReplayStorage, FileSystemReplayStorage>();

		return services;
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}