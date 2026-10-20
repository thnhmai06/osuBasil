using Basil.Application.Services.Contracts.Anticheat;
using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Content;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Multiplayer.Rooms;
using Basil.Application.Services.Contracts.Scores;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Contracts.Users;
using Basil.Application.Services.Implementations.Anticheat;
using Basil.Application.Services.Implementations.Beatmaps;
using Basil.Application.Services.Implementations.Chat;
using Basil.Application.Services.Implementations.Content;
using Basil.Application.Services.Implementations.Multiplayer;
using Basil.Application.Services.Implementations.Multiplayer.Rooms;
using Basil.Application.Services.Implementations.Scores;
using Basil.Application.Services.Implementations.Sessions;
using Basil.Application.Services.Implementations.Users;
using Basil.Application.Storage.Contracts.Chat;
using Basil.Application.Storage.Contracts.Content;
using Basil.Application.Storage.Contracts.Multiplayer.Matches;
using Basil.Application.Storage.Contracts.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Application.Services.Implementations;

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

		services.AddSingleton<IUserRegistry, UserRegistry>();
		services.AddSingleton<IGeneralChannelRegistry, GeneralChannelRegistry>();

		services.AddSingleton<SessionEventStream>();
		services.AddSingleton<ISessionEventSink>(sp => sp.GetRequiredService<SessionEventStream>());
		services.AddSingleton<SessionConnectionsService>();
		services.AddSingleton<SessionService>();
		services.AddSingleton<ISessionService>(sp => sp.GetRequiredService<SessionService>());

		services.AddSingleton<AuthService>();
		services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<AuthService>());

		services.AddSingleton<UserEventStream>();
		services.AddSingleton<UserService>();
		services.AddSingleton<IUserService>(sp => sp.GetRequiredService<UserService>());
		services.AddSingleton<IRoomUsersPort>(sp => sp.GetRequiredService<UserService>());

		services.AddSingleton<ChannelEventStream>();
		services.AddSingleton<IChannelEventSink>(sp => sp.GetRequiredService<ChannelEventStream>());
		services.AddSingleton<ChannelContext>(sp => new ChannelContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IChannelEventSink>(),
			sp.GetRequiredService<IGeneralChannelRegistry>(),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<IChannelRepository>()));
		services.AddSingleton<SessionContext>(sp => new SessionContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<ISessionEventSink>(),
			sp.GetRequiredService<IUserRegistry>(),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<ChannelContext>()));
		services.AddSingleton<ChannelService>();
		services.AddSingleton<IChannelService>(sp => sp.GetRequiredService<ChannelService>());

		services.AddSingleton<LobbyEventStream>();
		services.AddSingleton<ILobbyEventSink>(sp => sp.GetRequiredService<LobbyEventStream>());
		services.AddSingleton<RoomEventStream>();
		services.AddSingleton<IRoomEventSink>(sp => sp.GetRequiredService<RoomEventStream>());
		services.AddSingleton<LobbyContext>(sp => new LobbyContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<ILobbyEventSink>(),
			sp.GetRequiredService<IMatchRepository>(),
			sp.GetRequiredService<ISettingsRepository>(),
			sp.GetRequiredService<IRoomEventSink>(),
			sp.GetRequiredService<ChannelContext>(),
			sp.GetRequiredService<IRoomUsersPort>()));
		services.AddSingleton<Lobby>(sp => new Lobby(sp.GetRequiredService<LobbyContext>()));
		services.AddSingleton<LobbyService>();
		services.AddSingleton<ILobbyService>(sp => sp.GetRequiredService<LobbyService>());

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

		services.AddSingleton<BeatmapsetEventStream>();
		services.AddSingleton<BeatmapsetService>();
		services.AddSingleton<IBeatmapsetService>(sp => sp.GetRequiredService<BeatmapsetService>());

		services.AddSingleton<ScreenshotService>();
		services.AddSingleton<IScreenshotService>(sp => sp.GetRequiredService<ScreenshotService>());

		return services;
	}
}
