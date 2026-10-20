using Basil.Application.Objects.Anticheat;
using Basil.Application.Objects.Beatmaps;
using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Scores;
using Basil.Application.Objects.Sessions;
using Basil.Infrastructure.Runtime.Beatmaps;
using Basil.Infrastructure.Runtime.Events;
using Basil.Infrastructure.Runtime.Multiplayer;
using Basil.Infrastructure.Runtime.Scores;
using Basil.Infrastructure.Runtime.Sessions;
using Basil.Infrastructure.Runtime.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Infrastructure.Runtime;

/// <summary>Registers the background work that reacts to the application's events.</summary>
public static class DependencyInjection
{
	/// <summary>Adds one event pump for each service that emits events.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddInfrastructureRuntime(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.AddHostedService<RuntimeStartup>();

		services.AddSingleton<IEventHandler<UserEvent>, ConnectionHandler>();
		services.AddSingleton<IEventHandler<UserEvent>, RestrictionHandler>();
		services.AddSingleton<IEventHandler<ScoreEvent>, ScoreHandler>();
		services.AddSingleton<MatchRecorder>();
		services.AddSingleton<IEventHandler<RoomEvent>>(sp => sp.GetRequiredService<MatchRecorder>());
		services.AddSingleton<IEventHandler<LobbyEvent>>(sp => sp.GetRequiredService<MatchRecorder>());

		AddBus<UserEvent>(services);
		AddBus<ChannelEvent>(services);
		AddBus<LobbyEvent>(services);
		AddBus<RoomEvent>(services);
		AddBus<AnticheatEvent>(services);
		AddBus<ScoreEvent>(services);
		AddBus<BeatmapsetEvent>(services);

		services.AddHostedService<IdleConnectionSweeper>();
		services.AddHostedService<BeatmapsetStorageWatcher>();
		return services;
	}

	private static void AddBus<T>(IServiceCollection services) where T : Event
	{
		services.AddSingleton<EventBus<T>>();
		services.AddSingleton<IEventPublisher<T>>(sp => sp.GetRequiredService<EventBus<T>>());
		services.AddHostedService(sp => sp.GetRequiredService<EventBus<T>>());
	}
}