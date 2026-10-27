using Basil.Infrastructure.Runtime.Beatmaps;
using Basil.Infrastructure.Runtime.Connections;
using Basil.Infrastructure.Runtime.Startup;
using Basil.Common.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Runtime;

/// <summary>Registers background work owned by the runtime infrastructure.</summary>
public static class DependencyInjection
{
	/// <summary>Registers the runtime background work.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddInfrastructureRuntime(this IServiceCollection services)
	{
		services.AddCommonInfrastructure();
		services.AddHostedService<RuntimeStartup>();

		services.AddHostedService<IdleConnectionSweeper>();
		services.AddHostedService<BeatmapsetStorageWatcher>();
		return services;
	}
}