using Basil.Application.Runtime.Events;
using Basil.Infrastructure.Runtime.Beatmaps;
using Basil.Infrastructure.Runtime.Events;
using Basil.Infrastructure.Runtime.Sessions;
using Basil.Infrastructure.Runtime.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Infrastructure.Runtime;

/// <summary>Registers the background work that carries the application's events and keeps it running.</summary>
public static class DependencyInjection
{
	/// <summary>Registers the event dispatcher and the background work.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddInfrastructureRuntime(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.AddHostedService<RuntimeStartup>();

		services.AddHostedService<EventDispatcher>();

		services.AddHostedService<IdleConnectionSweeper>();
		services.AddHostedService<BeatmapsetStorageWatcher>();
		return services;
	}
}