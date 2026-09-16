using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Beatmaps;

/// <summary>Registers the Beatmaps slice's Application-layer services.</summary>
public static class BeatmapsServiceCollectionExtensions
{
	/// <summary>Registers the Beatmaps slice's Application-layer services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddBeatmapsApplication(this IServiceCollection services)
	{
		services.AddSingleton<DirectSearchService>();
		services.AddSingleton<MirrorService>();

		return services;
	}
}
