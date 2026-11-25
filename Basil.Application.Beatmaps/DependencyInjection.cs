using Basil.Application.Beatmaps.Logic;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Shared.Events;
using Basil.Application.Shared.Settings;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Beatmaps;

public static class DependencyInjection
{
	public static IServiceCollection AddBeatmapsApplication(this IServiceCollection services)
	{
		services.AddSingleton<BeatmapsetContext>(sp => new BeatmapsetContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<BeatmapsetEvent>>(),
			sp.GetRequiredService<IBeatmapsetRepository>(),
			sp.GetRequiredService<IBeatmapsetStorage>()));
		services.AddSingleton<BeatmapsetMatcher>();
		services.AddSingleton<BeatmapsetPlanner>();
		services.AddSingleton<BeatmapsetFileWriter>();
		services.AddSingleton<BeatmapsetFileAudit>();
		services.AddSingleton<BeatmapsetStorer>();
		services.AddSingleton<BeatmapsetImporter>();
		services.AddSingleton<BeatmapsetScanner>();
		services.AddSingleton<Beatmapsets>(sp => new Beatmapsets(
			sp.GetRequiredService<BeatmapsetContext>(),
			sp.GetRequiredService<BeatmapsetImporter>(),
			sp.GetRequiredService<BeatmapsetScanner>(),
			sp.GetRequiredService<IBeatmapRepository>(),
			sp.GetRequiredService<ISender>(),
			sp.GetRequiredService<ISettingsRepository>()));
		services.AddSingleton<BeatmapReloads>(sp => new BeatmapReloads(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IBeatmapReloader>()));
		return services;
	}
}