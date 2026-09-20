using Basil.Application.Unresolved.Sessions;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Unresolved.Spectating;

/// <summary>Registers the Spectating slice's Application-layer services.</summary>
public static class SpectatingServiceCollectionExtensions
{
	/// <summary>Registers the Spectating slice's Application-layer services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddSpectatingApplication(this IServiceCollection services)
	{
		services.AddSingleton<SpectatorService>();

		services.AddSingleton<IPlayerInputEvents, PlayerInputEvents>();
		services.AddSingleton<IPlayerStatusEvents, PlayerStatusEvents>();

		services.AddSingleton<IPlayerLogoutHandler, SpectatorTeardownLogoutHandler>();
		services.AddSingleton<IPlayerLogoutHandler, StatusPublishLogoutHandler>();

		return services;
	}
}