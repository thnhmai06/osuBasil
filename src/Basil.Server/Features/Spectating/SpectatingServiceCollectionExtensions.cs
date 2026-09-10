using Basil.Server.Features.Spectating.Packets;
using Basil.Server.Shared.Http.Bancho;
using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Spectating;

/// <summary>Registers the Spectating slice's services.</summary>
public static class SpectatingServiceCollectionExtensions
{
	/// <summary>Registers the Spectating slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddSpectating(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<SpectatorService>();

		services.AddSingleton<IPlayerInputEvents, PlayerInputEvents>();
		services.AddSingleton<IPlayerStatusEvents, PlayerStatusEvents>();

		services.AddSingleton<IPlayerLogoutHandler, SpectatorTeardownLogoutHandler>();
		services.AddSingleton<IPlayerLogoutHandler, StatusPublishLogoutHandler>();

		services.AddSingleton<IPacketHandler, StartSpectatingHandler>();
		services.AddSingleton<IPacketHandler, StopSpectatingHandler>();
		services.AddSingleton<IPacketHandler, SpectateFramesHandler>();
		services.AddSingleton<IPacketHandler, CantSpectateHandler>();

		return services;
	}
}