using Basil.Application.Services.Contracts.Anticheat;
using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Events;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Events;
using Basil.Application.Services.Contracts.Scores;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Contracts.Users;
using Basil.Infrastructure.Runtime.Events;
using Microsoft.Extensions.DependencyInjection;
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
		AddPump<ISessionService, UserEvent>(services);
		AddPump<IUserService, UserEvent>(services);
		AddPump<IChannelService, ChannelEvent>(services);
		AddPump<ILobbyService, LobbyEvent>(services);
		AddPump<IRoomService, RoomEvent>(services);
		AddPump<IAnticheatService, AnticheatEvent>(services);
		AddPump<IScoreService, ScoreEvent>(services);
		AddPump<IBeatmapsetService, BeatmapsetEvent>(services);
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
