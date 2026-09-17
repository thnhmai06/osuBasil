using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Irc;

/// <summary>Registers the IRC TCP transport's hosted services.</summary>
public static class IrcHostServiceCollectionExtensions
{
	/// <summary>Registers the IRC TCP transport's hosted services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddIrcHost(this IServiceCollection services)
	{
		services.AddHostedService<TcpIrcListener>();
		services.AddHostedService<IrcMetricsPublisher>();

		return services;
	}
}