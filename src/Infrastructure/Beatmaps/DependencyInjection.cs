using Basil.Application.Beatmaps.Capabilities;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Storage;
using Basil.Infrastructure.Beatmaps.Capabilities;
using Basil.Infrastructure.Beatmaps.Persistence;
using Basil.Infrastructure.Beatmaps.Storage;
using Basil.Infrastructure.Beatmaps.Workers;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Registry;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Beatmaps;

/// <summary>Registers the persistence and capability implementations owned by Beatmaps.</summary>
public static class DependencyInjection
{
	/// <summary>Adds Beatmaps persistence and capability implementations.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services"/>, for chaining.</returns>
	public static IServiceCollection AddBeatmapsInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<BeatmapsDbContext>>();
		services.AddSingleton<IResident, IncomingLeftovers>();
		services.AddSingleton<IBeatmapsetRepository, PostgresBeatmapsetRepository>();
		services.AddSingleton<PostgresBeatmapRepository>();
		services.AddSingleton<IBeatmapRepository>(provider => provider.GetRequiredService<PostgresBeatmapRepository>());
		services.AddSingleton(provider =>
			new Lazy<PostgresBeatmapRepository>(provider.GetRequiredService<PostgresBeatmapRepository>));
		services.AddSingleton<FileBeatmapsetStorage>();
		services.AddSingleton<IBeatmapsetStorage>(provider => provider.GetRequiredService<FileBeatmapsetStorage>());
		services.AddSingleton<IBeatmapsetReader, OsuBeatmapsetReader>();
		services.AddSingleton<IBeatmapAnalyser, OsuBeatmapAnalyser>();
		services.AddSingleton<IBeatmapAssets, BeatmapPreviewMaker>();
		services.AddSingleton<IBeatmapsetMirror, HttpBeatmapsetMirror>();
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