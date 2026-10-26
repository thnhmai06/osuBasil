using Basil.Application.Capabilities;
using Basil.Application.Capabilities.Beatmaps;
using Basil.Application.Capabilities.Content;
using Basil.Application.Capabilities.Scores;
using Basil.Application.Capabilities.Users;
using Basil.Application.Persistence.Auth;
using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Persistence.Chat;
using Basil.Application.Persistence.Content;
using Basil.Application.Persistence.Multiplayer;
using Basil.Application.Persistence.Users;
using Basil.Application.Runtime.Anticheat;
using Basil.Application.Runtime.Beatmaps;
using Basil.Application.Runtime.Chat;
using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Application.Runtime.Scores;
using Basil.Application.Runtime.Servers;
using Basil.Application.Runtime.Sessions;
using Basil.Application.Runtime.Users;
using Basil.Domain.Users;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Application.Runtime;

/// <summary>Registers the application: its objects, registries and root objects, and the services they use.</summary>
public static class DependencyInjection
{
	/// <summary>
	///     Adds the application, which uses the repository, storage and capability ports the caller registers.
	/// </summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddApplication(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.AddApplicationCapabilities();
		services.AddEvents();

		services.AddSingleton(new GeneralChannelRegistry());

		// The contexts and the registry refer to each other, so the contexts resolve the registry at first use.
		services.AddSingleton<UserContext>(sp => new UserContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<UserEvent>>(),
			new Lazy<UserRegistry>(sp.GetRequiredService<UserRegistry>),
			sp.GetRequiredService<IUserRepository>(),
			sp.GetRequiredService<IRestrictionRepository>(),
			sp.GetRequiredService<IModerationLogRepository>(),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<ILoginRepository>(),
			sp.GetRequiredService<ICredentialRepository>(),
			sp.GetRequiredService<ChannelContext>()));
		services.AddSingleton<SessionContext>(sp => new SessionContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<UserEvent>>(),
			new Lazy<UserRegistry>(sp.GetRequiredService<UserRegistry>),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<ChannelContext>()));
		services.AddSingleton<AnticheatReporter>(sp => new AnticheatReporter(
			sp.GetRequiredService<IEventPublisher<AnticheatEvent>>()));
		services.AddSingleton<IScoreBeatmapAcceptance>(sp => sp.GetRequiredService<Lobby>());
		services.AddSingleton<ScoreSubmitter>(sp => new ScoreSubmitter(
			sp.GetRequiredService<ScoreRecorder>(),
			sp.GetRequiredService<IEventPublisher<ScoreEvent>>(),
			sp.GetRequiredService<IScoreBeatmapAcceptance>(),
			sp.GetRequiredService<TimeProvider>()));
		services.AddSingleton<UserRegistry>(sp => new UserRegistry(
			sp.GetRequiredService<AccountService>(),
			sp.GetRequiredService<UserContext>(),
			sp.GetRequiredService<SessionContext>()));
		services.AddSingleton<IRoomUsersPort>(sp => sp.GetRequiredService<UserRegistry>());
		services.AddSingleton<Func<UserRecord, User>>(sp => sp.GetRequiredService<UserRegistry>().Of);

		services.AddSingleton<ChannelContext>(sp => new ChannelContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<ChannelEvent>>(),
			sp.GetRequiredService<GeneralChannelRegistry>(),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<IChannelRepository>()));
		services.AddSingleton<ServerContext>(sp => new ServerContext(
			sp.GetRequiredService<SessionContext>(),
			sp.GetRequiredService<IScreenshotInspector>(),
			sp.GetRequiredService<IScreenshotStorage>(),
			sp.GetRequiredService<Lobby>(),
			sp.GetRequiredService<IUserRepository>()));
		services.AddSingleton<Server>(sp => new Server(sp.GetRequiredService<ServerContext>()));
		services.AddSingleton<LobbyContext>(sp => new LobbyContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<LobbyEvent>>(),
			sp.GetRequiredService<IMatchRepository>(),
			sp.GetRequiredService<IRoundRepository>(),
			sp.GetRequiredService<IUserRepository>(),
			sp.GetRequiredService<ISettingsRepository>(),
			sp.GetRequiredService<IEventPublisher<RoomEvent>>(),
			sp.GetRequiredService<ChannelContext>(),
			sp.GetRequiredService<IRoomUsersPort>()));
		services.AddSingleton<Lobby>(sp => new Lobby(sp.GetRequiredService<LobbyContext>()));

		services.AddSingleton<BeatmapsetContext>(sp => new BeatmapsetContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<BeatmapsetEvent>>(),
			sp.GetRequiredService<IBeatmapsetRepository>(),
			sp.GetRequiredService<IBeatmapsetStorage>()));
		services.AddSingleton<Beatmapsets>(sp => new Beatmapsets(
			sp.GetRequiredService<BeatmapsetContext>(),
			sp.GetRequiredService<BeatmapsetImporter>(),
			sp.GetRequiredService<BeatmapsetScanner>()));

		return services;
	}

	/// <summary>
	///     Registers the publishing of events: every category is published by the one publisher, and the handlers
	///     found in this assembly observe the events they name.
	/// </summary>
	internal static IServiceCollection AddEvents(this IServiceCollection services)
	{
		services.AddMediator(options =>
		{
			options.ServiceLifetime = ServiceLifetime.Singleton;
			options.GenerateTypesAsInternal = true;
			options.NotificationPublisherType = typeof(QueuedNotificationPublisher);
		});
		services.AddSingleton<EventQueue>();
		services.AddSingleton(sp => new EventPublisher(new Lazy<IPublisher>(sp.GetRequiredService<IPublisher>),
			sp.GetRequiredService<TimeProvider>()));
		AddCategory<UserEvent>(services);
		AddCategory<ChannelEvent>(services);
		AddCategory<LobbyEvent>(services);
		AddCategory<RoomEvent>(services);
		AddCategory<AnticheatEvent>(services);
		AddCategory<ScoreEvent>(services);
		AddCategory<BeatmapsetEvent>(services);
		return services;
	}

	private static void AddCategory<T>(IServiceCollection services) where T : Event
	{
		services.AddSingleton<IEventPublisher<T>>(sp => sp.GetRequiredService<EventPublisher>());
	}
}