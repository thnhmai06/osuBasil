using Basil.Application.Objects.Anticheat;
using Basil.Application.Objects.Beatmaps;
using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Application.Objects.Scores;
using Basil.Application.Objects.Servers;
using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Users;
using Basil.Application.Services;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Scores;
using Basil.Application.Services.Users;
using Basil.Application.Storage.Auth;
using Basil.Application.Storage.Beatmaps;
using Basil.Application.Storage.Chat;
using Basil.Application.Storage.Content;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Users;
using Basil.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Application.Objects;

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
		services.AddApplicationServices();

		services.AddSingleton(new GeneralChannelRegistry());

		services.AddSingleton<UserRegistry>(sp => new UserRegistry(
			sp.GetRequiredService<AccountService>(),
			sp.GetRequiredService<IUserRepository>(),
			sp.GetRequiredService<IRestrictionRepository>(),
			sp.GetRequiredService<IModerationLogRepository>(),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<ILoginRepository>(),
			sp.GetRequiredService<ICredentialRepository>(),
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<UserEvent>>(),
			sp.GetRequiredService<ChannelContext>(),
			sp.GetRequiredService<Lobby>,
			sp.GetRequiredService<ScoreRecorder>(),
			sp.GetRequiredService<IEventPublisher<ScoreEvent>>(),
			sp.GetRequiredService<IEventPublisher<AnticheatEvent>>()));
		services.AddSingleton<IRoomUsersPort>(sp => sp.GetRequiredService<UserRegistry>());
		services.AddSingleton<Func<UserRecord, User>>(sp => sp.GetRequiredService<UserRegistry>().Of);

		services.AddSingleton<ChannelContext>(sp => new ChannelContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<ChannelEvent>>(),
			sp.GetRequiredService<GeneralChannelRegistry>(),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<IChannelRepository>()));
		services.AddSingleton<SessionContext>(sp => sp.GetRequiredService<UserRegistry>().Session);
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
			sp.GetRequiredService<IMatchEventRepository>(),
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
}