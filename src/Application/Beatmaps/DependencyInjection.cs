using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Beatmaps.Storage;
using Basil.Application.Shared.Events;
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
		services.AddSingleton<Beatmapsets>(sp => new Beatmapsets(
			sp.GetRequiredService<BeatmapsetContext>(),
			sp.GetRequiredService<IBeatmapsetImporter>(),
			sp.GetRequiredService<IBeatmapsetScanner>()));
		services.AddSingleton<Rulesets>(sp => new Rulesets(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IRulesetUpdater>(),
			sp.GetRequiredService<IBeatmapReloader>()));
		return services;
	}
}