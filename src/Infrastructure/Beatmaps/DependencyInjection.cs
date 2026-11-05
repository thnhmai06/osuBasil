using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Beatmaps.Storage;
using Basil.Infrastructure.Beatmaps.Adapters;
using Basil.Infrastructure.Beatmaps.Logic;
using Basil.Infrastructure.Beatmaps.Persistence;
using Basil.Infrastructure.Beatmaps.Services;
using Basil.Infrastructure.Beatmaps.Storage;
using Basil.Infrastructure.Beatmaps.Workers;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Common.Registry;
using Basil.Infrastructure.Common.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>Registers the persistence and capability implementations owned by Beatmaps.</summary>
public static class DependencyInjection
{
	/// <summary>Adds Beatmaps persistence and capability implementations.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBeatmapsInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<BeatmapsDbContext>>();
		services.AddSingleton<IResident, IncomingLeftovers>();
		services.AddSingleton<IBeatmapsetRepository, EfBeatmapsetRepository>();
		services.AddSingleton<IBeatmapRepository, EfBeatmapRepository>();
		services.AddSingleton<FileBeatmapsetStorage>();
		services.AddSingleton<IBeatmapsetStorage>(provider => provider.GetRequiredService<FileBeatmapsetStorage>());
		services.AddSingleton<IBeatmapsetReader, OsuBeatmapsetReader>();
		services.AddSingleton<IBeatmapAnalyser, OsuBeatmapAnalyser>();
		services.AddSingleton<IBeatmapAssets, BeatmapPreviewMaker>();
		services.AddSingleton<IBeatmapsetMirror, HttpBeatmapsetMirror>();
		services.AddSingleton<RulesetStore>();
		services.AddSingleton<IRulesetUpdater>(provider => new RulesetUpdater(
			provider.GetRequiredService<RulesetStore>(), new HttpClient { Timeout = TimeSpan.FromMinutes(5) },
			provider.GetRequiredService<ILogger<RulesetUpdater>>()));
		services.AddSingleton<IBeatmapReloader, BeatmapReloader>();
		services.AddOptions<AssetOptions>().PostConfigure<DataPaths>((options, paths) =>
		{
			if (string.IsNullOrWhiteSpace(options.CacheDirectory))
				options.CacheDirectory = Path.Combine(paths.Cache.Path, "Previews");
		});
		services.AddBeatmapsLogic();
		return services;
	}

	/// <summary>Reads the asset settings (<c>Basil:Assets</c>) once, so a later edit of the files waits for the next start.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <param name="configuration">The configuration to read.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBeatmapsInfrastructureOptions(this IServiceCollection services,
		IConfiguration configuration)
	{
		services.AddOptions<AssetOptions>().Bind(configuration.GetSection("Basil:Assets"));
		return services;
	}

	/// <summary>Adds the rules that import, plan, store and scan beatmapsets.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBeatmapsLogic(this IServiceCollection services)
	{
		services.AddSingleton<DifficultyAnalyser>();
		services.AddSingleton<BeatmapsetMatcher>();
		services.AddSingleton<BeatmapsetPlanner>();
		services.AddSingleton<BeatmapsetFileWriter>();
		services.AddSingleton<BeatmapsetFileAudit>();
		services.AddSingleton<BeatmapsetStorer>();
		services.AddSingleton<IBeatmapsetImporter, BeatmapsetImporter>();
		services.AddSingleton<IBeatmapsetScanner, BeatmapsetScanner>();
		return services;
	}

	/// <summary>Adds the background work of Beatmaps: keeping the beatmapsets in step with their stored files.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBeatmapsBackgroundWork(this IServiceCollection services)
	{
		services.AddHostedService<BeatmapsetStorageWatcher>();
		return services;
	}
}