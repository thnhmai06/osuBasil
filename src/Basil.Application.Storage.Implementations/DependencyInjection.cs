using Basil.Application.Storage.Chat;
using Basil.Application.Storage.Implementations.Chat;
using Basil.Application.Storage.Implementations.Multiplayer;
using Basil.Application.Storage.Implementations.Sessions;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Storage.Implementations;

/// <summary>Registers the application's in-memory storage: the registries of what is online.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the user registry, the general channel registry and the lobby.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddApplicationStorage(this IServiceCollection services)
	{
		services.AddSingleton<IUserRegistry, UserRegistry>();
		services.AddSingleton<IGeneralChannelRegistry, GeneralChannelRegistry>();
		services.AddSingleton<ILobby, Lobby>();
		return services;
	}
}
