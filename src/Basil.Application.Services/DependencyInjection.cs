using Basil.Application.Contracts.Anticheat;
using Basil.Application.Contracts.Beatmaps;
using Basil.Application.Contracts.Chat;
using Basil.Application.Contracts.Multiplayer;
using Basil.Application.Contracts.Scores;
using Basil.Application.Contracts.Sessions;
using Basil.Application.Contracts.Users;
using Basil.Application.Services.Anticheat;
using Basil.Application.Services.Beatmaps;
using Basil.Application.Services.Chat;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Multiplayer.Rooms;
using Basil.Application.Services.Scores;
using Basil.Application.Services.Sessions;
using Basil.Application.Services.Users;
using Microsoft.Extensions.DependencyInjection;

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
		services.AddSingleton<SessionService>();
		services.AddSingleton<ISessionService>(sp => sp.GetRequiredService<SessionService>());

		services.AddSingleton<AuthService>();
		services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<AuthService>());

		services.AddSingleton<UserService>();
		services.AddSingleton<IUserService>(sp => sp.GetRequiredService<UserService>());

		services.AddSingleton<ChannelEventStream>();
		services.AddSingleton<ChannelSpectatorService>();
		services.AddSingleton<ChannelService>();
		services.AddSingleton<IChannelService>(sp => sp.GetRequiredService<ChannelService>());

		services.AddSingleton<LobbyService>();
		services.AddSingleton<ILobbyService>(sp => sp.GetRequiredService<LobbyService>());

		services.AddSingleton<RoomEventStream>();
		services.AddSingleton<RoomChannelService>();
		services.AddSingleton<RoomMembershipService>();
		services.AddSingleton<RoomAuthorityService>();
		services.AddSingleton<RoomSettingsService>();
		services.AddSingleton<RoomSlotsService>();
		services.AddSingleton<RoomRoundsService>();
		services.AddSingleton<RoomService>();
		services.AddSingleton<IRoomService>(sp => sp.GetRequiredService<RoomService>());

		services.AddSingleton<AnticheatService>();
		services.AddSingleton<IAnticheatService>(sp => sp.GetRequiredService<AnticheatService>());

		services.AddSingleton<MatchService>();
		services.AddSingleton<IMatchService>(sp => sp.GetRequiredService<MatchService>());

		services.AddSingleton<ScoreService>();
		services.AddSingleton<IScoreService>(sp => sp.GetRequiredService<ScoreService>());

		services.AddSingleton<BeatmapsetService>();
		services.AddSingleton<IBeatmapsetService>(sp => sp.GetRequiredService<BeatmapsetService>());

		return services;
	}
}