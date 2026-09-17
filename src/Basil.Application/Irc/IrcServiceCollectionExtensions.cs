using Basil.Application.Sessions;
using Basil.Application.Shared.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Irc;

/// <summary>Registers the Irc slice's Application-layer services.</summary>
public static class IrcServiceCollectionExtensions
{
	/// <summary>Registers the Irc slice's Application-layer services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddIrcApplication(this IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<IrcOptions>(configuration.GetSection(IrcOptions.SectionName));

		services.AddSingleton<IrcAuthenticationService>();
		services.AddSingleton<IrcQueryService>();

		services.AddSingleton<ISessionRegistry<IrcSession>, IrcSessionRegistry>();
		services.AddSingleton<IPlayerLogoutHandler, IrcSessionRemovalLogoutHandler>();

		return services;
	}
}