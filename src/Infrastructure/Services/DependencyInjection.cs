using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Infrastructure.Services.Beatmaps;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Services;

/// <summary>Registers the capability ports that reach outside the process.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the capability ports the server needs.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
	{
		services.AddSingleton<IBeatmapsetReader, OsuBeatmapsetReader>();
		services.AddSingleton<IBeatmapAnalyser, OsuBeatmapAnalyser>();
		services.AddSingleton<IBeatmapAssets, BeatmapAssets>();
		services.AddSingleton<IBeatmapsetMirror, HttpBeatmapsetMirror>();
		return services;
	}
}
