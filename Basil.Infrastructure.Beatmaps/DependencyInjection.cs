using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Rulesets.Ports;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Beatmaps.Storage;
using Basil.Infrastructure.Beatmaps.Adapters;
using Basil.Infrastructure.Beatmaps.Logic;
using Basil.Infrastructure.Beatmaps.Persistence;
using Basil.Infrastructure.Beatmaps.Services;
using Basil.Infrastructure.Beatmaps.Storage;
using Basil.Infrastructure.Beatmaps.Workers;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Shared.Registry;
using Basil.Infrastructure.Shared.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Beatmaps;

public static class DependencyInjection
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddBeatmapsInfrastructure()
		{
			services.AddSingleton<IModuleMigration, ModuleMigration<BeatmapsDbContext>>();
			services.AddSingleton<IResident, IncomingLeftovers>();
			services.AddSingleton<IBeatmapsetRepository, EfBeatmapsetRepository>();
			services.AddSingleton<IBeatmapRepository, EfBeatmapRepository>();
			services.AddSingleton<FileBeatmapsetStorage>();
			services.AddSingleton<IBeatmapsetStorage>(provider => provider.GetRequiredService<FileBeatmapsetStorage>());
			services.AddSingleton<IBeatmapsetReader, OsuBeatmapsetReader>();
			services.AddSingleton<IBeatmapFiles, BeatmapFiles>();
			services.AddSingleton<IBeatmapAssets, BeatmapPreviewMaker>();
			services.AddSingleton<IBeatmapReloader, BeatmapReloader>();
			services.AddOptions<AssetOptions>().PostConfigure<DataPaths>((options, paths) =>
			{
				if (string.IsNullOrWhiteSpace(options.CacheDirectory))
					options.CacheDirectory = Path.Combine(paths.Cache.Path, "Previews");
			});
			services.AddBeatmapsLogic();
			return services;
		}

		public IServiceCollection AddBeatmapsInfrastructureOptions(IConfiguration configuration)
		{
			services.AddOptions<AssetOptions>().Bind(configuration.GetSection("Basil:Assets"));
			return services;
		}

		public IServiceCollection AddBeatmapsLogic()
		{
			services.AddSingleton<BeatmapsetMatcher>();
			services.AddSingleton<BeatmapsetPlanner>();
			services.AddSingleton<BeatmapsetFileWriter>();
			services.AddSingleton<BeatmapsetFileAudit>();
			services.AddSingleton<BeatmapsetStorer>();
			services.AddSingleton<IBeatmapsetImporter, BeatmapsetImporter>();
			services.AddSingleton<IBeatmapsetScanner, BeatmapsetScanner>();
			return services;
		}

		public IServiceCollection AddBeatmapsBackgroundWork()
		{
			services.AddHostedService<BeatmapsetStorageWatcher>();
			return services;
		}
	}
}