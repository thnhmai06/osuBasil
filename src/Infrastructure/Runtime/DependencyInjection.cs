using Basil.Application.Objects.Anticheat;
using Basil.Application.Objects.Beatmaps;
using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Scores;
using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Users;
using Basil.Infrastructure.Runtime.Beatmaps;
using Basil.Infrastructure.Runtime.Events;
using Basil.Infrastructure.Runtime.Multiplayer;
using Basil.Infrastructure.Runtime.Scores;
using Basil.Infrastructure.Runtime.Sessions;
using Basil.Infrastructure.Runtime.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

		AddPump<ISessionService, UserEvent>(services);
		AddPump<IUserService, UserEvent>(services);
		AddPump<IChannelService, ChannelEvent>(services);
		AddPump<ILobbyService, LobbyEvent>(services);
		AddPump<IRoomService, RoomEvent>(services);
		AddPump<IAnticheatService, AnticheatEvent>(services);
		AddPump<IScoreService, ScoreEvent>(services);
		AddPump<IBeatmapsetService, BeatmapsetEvent>(services);

		services.AddHostedService<IdleConnectionSweeper>();
		services.AddHostedService<BeatmapsetStorageWatcher>();
		return services;
	}

	private static void AddPump<TService, TEvent>(IServiceCollection services)
		where TService : class, IEventPublisher<TEvent>
		where TEvent : Event
	{
		services.AddSingleton<IHostedService>(services =>
			new EventPump<TEvent>(
				services.GetRequiredService<TService>(),
				services.GetServices<IEventHandler<TEvent>>(),
				services.GetRequiredService<ILogger<EventPump<TEvent>>>()));
	}
}