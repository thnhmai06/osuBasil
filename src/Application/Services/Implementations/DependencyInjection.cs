using Basil.Application.Services.Contracts.Anticheat;
using Basil.Application.Services.Contracts.Beatmaps;
using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Scores;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Services.Contracts.Users;
using Basil.Application.Services.Implementations.Anticheat;
using Basil.Application.Services.Implementations.Beatmaps;
using Basil.Application.Services.Implementations.Chat;
using Basil.Application.Services.Implementations.Multiplayer;
using Basil.Application.Services.Implementations.Multiplayer.Rooms;
using Basil.Application.Services.Implementations.Scores;
using Basil.Application.Services.Implementations.Sessions;
using Basil.Application.Services.Implementations.Users;
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