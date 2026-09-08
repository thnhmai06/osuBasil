using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Sessions;

namespace Basil.Server.Features.Irc;

/// <summary>Registers the Irc slice's services.</summary>
public static class IrcServiceCollectionExtensions
{
	/// <summary>Registers the Irc slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddIrc(this IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<IrcOptions>(configuration.GetSection(IrcOptions.SectionName));

		services.AddSingleton<IrcAuthenticationService>();
		services.AddSingleton<IrcQueryService>();

		services.AddSingleton<ISessionRegistry<IrcSession>, IrcSessionRegistry>();

		services.AddHostedService<TcpIrcListener>();

		return services;
	}
}