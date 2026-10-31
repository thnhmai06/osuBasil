using Basil.Application.Common.Events;
using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Scores.Objects;
using Basil.Application.Users.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Multiplayer;

/// <summary>Registers the runtime objects owned by Multiplayer.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the Multiplayer runtime objects.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddMultiplayerApplication(this IServiceCollection services)
	{
		services.AddSingleton<IScoreBeatmapAcceptance>(sp => sp.GetRequiredService<Lobby>());
		services.AddSingleton<RoomBlocks>(sp => new RoomBlocks(
			sp.GetRequiredService<IRelationshipRepository>()));
		services.AddSingleton<LobbyContext>(sp => new LobbyContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<LobbyEvent>>(),
			sp.GetRequiredService<IMatchRepository>(),
			sp.GetRequiredService<IRoundRepository>(),
			sp.GetRequiredService<IUserRepository>(),
			sp.GetRequiredService<ISettingsRepository>(),
			sp.GetRequiredService<IEventPublisher<RoomEvent>>(),
			sp.GetRequiredService<RoomBlocks>()));
		services.AddSingleton<Lobby>(sp => new Lobby(sp.GetRequiredService<LobbyContext>()));
		return services;
	}
}