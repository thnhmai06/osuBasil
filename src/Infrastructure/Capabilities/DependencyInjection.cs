using Basil.Application.Capabilities.Content;
using Basil.Application.Capabilities.Beatmaps;
using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Persistence.Content;
using Basil.Infrastructure.Capabilities.Beatmaps;
using Basil.Infrastructure.Capabilities.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Capabilities;

/// <summary>Registers the capability ports that reach outside the process.</summary>
public static class DependencyInjection
{
	/// <summary>Registers the capability ports that reach outside the process.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddInfrastructureCapabilities(this IServiceCollection services)
	{
		services.AddSingleton<IBeatmapsetReader, OsuBeatmapsetReader>();
		services.AddSingleton<IBeatmapAnalyser, OsuBeatmapAnalyser>();
		services.AddSingleton<IBeatmapAssets, BeatmapAssetStore>();
		services.AddSingleton<IBeatmapsetMirror, HttpBeatmapsetMirror>();
		services.AddSingleton<IScreenshotInspector, ImageSharpScreenshotInspector>();
		return services;
	}
}