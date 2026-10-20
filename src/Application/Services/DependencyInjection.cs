using Basil.Application.Objects.Beatmaps;
using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Multiplayer.Events;
using Basil.Application.Objects.Multiplayer.Rooms;
using Basil.Application.Objects.Scores;
using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Servers;
using Basil.Application.Objects.Users;
using Basil.Application.Services.Anticheat;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Chat;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Multiplayer.Rooms;
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

namespace Basil.Application.Services;

/// <summary>Registers the application's registries and services.</summary>
public static class DependencyInjection
{
	/// <summary>
	///     Adds the services that use the repository and storage ports, which the caller registers.
	/// </summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddApplicationServices(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);

		services.AddSingleton<IGeneralChannelRegistry, GeneralChannelRegistry>();

		services.AddSingleton<AccountService>();
		services.AddSingleton<IAccountService>(sp => sp.GetRequiredService<AccountService>());
		services.AddSingleton<UserRegistry>(sp => new UserRegistry(
			sp.GetRequiredService<IAccountService>(),
			sp.GetRequiredService<IUserRepository>(),
			sp.GetRequiredService<IRestrictionRepository>(),
			sp.GetRequiredService<IModerationLogRepository>(),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<ILoginRepository>(),
			sp.GetRequiredService<ICredentialRepository>(),
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<UserEvent>>(),
			sp.GetRequiredService<ChannelContext>()));
		services.AddSingleton<IRoomUsersPort>(sp => sp.GetRequiredService<UserRegistry>());
		services.AddSingleton<Func<UserRecord, User>>(sp => sp.GetRequiredService<UserRegistry>().Of);

		services.AddSingleton<ChannelContext>(sp => new ChannelContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<ChannelEvent>>(),
			sp.GetRequiredService<IGeneralChannelRegistry>(),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<IChannelRepository>()));
		services.AddSingleton<SessionContext>(sp => new SessionContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<UserEvent>>(),
			sp.GetRequiredService<UserRegistry>(),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<ChannelContext>()));
		services.AddSingleton<ServerContext>(sp => new ServerContext(
			sp.GetRequiredService<SessionContext>(),
			sp.GetRequiredService<IScreenshotInspector>(),
			sp.GetRequiredService<IScreenshotStorage>(),
			sp.GetRequiredService<Lobby>(),
			sp.GetRequiredService<IUserRepository>()));
		services.AddSingleton<Server>(sp => new Server(sp.GetRequiredService<ServerContext>()));
		services.AddSingleton<ChannelService>();
		services.AddSingleton<IChannelService>(sp => sp.GetRequiredService<ChannelService>());

		services.AddSingleton<LobbyContext>(sp => new LobbyContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<LobbyEvent>>(),
			sp.GetRequiredService<IMatchRepository>(),
			sp.GetRequiredService<ISettingsRepository>(),
			sp.GetRequiredService<IEventPublisher<RoomEvent>>(),
			sp.GetRequiredService<ChannelContext>(),
			sp.GetRequiredService<IRoomUsersPort>()));
		services.AddSingleton<Lobby>(sp => new Lobby(sp.GetRequiredService<LobbyContext>()));

		services.AddSingleton<RoomMembershipService>();
		services.AddSingleton<RoomRoundsService>();
		services.AddSingleton<RoomService>();
		services.AddSingleton<IRoomService>(sp => sp.GetRequiredService<RoomService>());

		services.AddSingleton<AnticheatService>();
		services.AddSingleton<IAnticheatService>(sp => sp.GetRequiredService<AnticheatService>());

		services.AddSingleton<MatchService>();
		services.AddSingleton<IMatchService>(sp => sp.GetRequiredService<MatchService>());

		services.AddSingleton<ScoreService>();
		services.AddSingleton<IScoreService>(sp => sp.GetRequiredService<ScoreService>());

		services.AddSingleton<BeatmapsetIndexer>();
		services.AddSingleton<IBeatmapsetIndexing>(sp => sp.GetRequiredService<BeatmapsetIndexer>());
		services.AddSingleton<BeatmapsetContext>(sp => new BeatmapsetContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<BeatmapsetEvent>>(),
			sp.GetRequiredService<IBeatmapsetRepository>(),
			sp.GetRequiredService<IBeatmapsetStorage>()));
		services.AddSingleton<Beatmapsets>(sp => new Beatmapsets(
			sp.GetRequiredService<BeatmapsetContext>(),
			sp.GetRequiredService<IBeatmapsetIndexing>()));

		return services;
	}
}