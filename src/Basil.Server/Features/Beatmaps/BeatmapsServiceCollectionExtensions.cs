using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Basil.Server.Features.Beatmaps;

/// <summary>Registers the Beatmaps slice's services and endpoints.</summary>
public static class BeatmapsServiceCollectionExtensions
{
	/// <summary>Registers the Beatmaps slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddBeatmaps(this IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<MirrorOptions>(configuration.GetSection(MirrorOptions.SectionName));

		services.AddSingleton<DirectSearchService>();
		services.AddSingleton<MirrorService>();

		services.AddSingleton<IBeatmapRepository>(sp =>
			new CachingBeatmapRepository(
				new SqliteBeatmapRepository(BuildConnectionString(sp),
					sp.GetRequiredService<ILogger<SqliteBeatmapRepository>>()),
				sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<ILogger<CachingBeatmapRepository>>()));
		services.AddSingleton<IBeatmapsetRepository>(sp =>
			new CachingBeatmapsetRepository(
				new SqliteBeatmapsetRepository(BuildConnectionString(sp),
					sp.GetRequiredService<ILogger<SqliteBeatmapsetRepository>>()),
				sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<ILogger<CachingBeatmapsetRepository>>()));

		services.AddHttpClient<IMirrorSearchClient, HttpMirrorSearchClient>();

		services.AddSingleton<IOsuCalculator, PpyOsuCalculator>();
		services.AddSingleton<BeatmapsetAssetCache>();
		services.AddSingleton<BeatmapIngestionService>();

		services.AddHostedService<BeatmapWatcherService>();
		services.AddHostedService<BeatmapsetGarbageCollectorService>();
		services.AddHostedService<BeatmapsetMigrationService>();

		return services;
	}

	/// <summary>Maps the Beatmaps slice's routes onto the `api.` host.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapBeatmapsRoutes(this RouteGroupBuilder group)
	{
		group.MapBeatmapsetRoutes();
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}