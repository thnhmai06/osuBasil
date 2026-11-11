using Basil.Gateway.RateLimiting;
using Basil.Host.Irc.Commands;
using Basil.Host.Irc.Session;
using Basil.Host.Shared.Documentation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Host.Irc.Transport;

/// <summary>Registers the IRC host.</summary>
public static class IrcHost
{
	/// <summary>Adds the services the IRC host needs, including the handlers that deliver chat to IRC clients.</summary>
	/// <param name="services">The service collection.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddIrcHost(this IServiceCollection services)
	{
		services.TryAddSingleton<RateLimiter>();
		services.TryAddSingleton<LoginAttemptGuard>();
		services.AddSingleton<IrcClients>();
		services.AddSingleton<IrcPresence>();
		services.AddSingleton<IrcState>();
		services.AddSingleton<IrcCommands>();
		services.AddSingleton<IDocumentPage, IrcReference>();
		return services;
	}

	/// <summary>
	///     Adds the socket listener, which is what makes the server answer on the IRC port; the port is
	///     <c>Basil:Server:Irc:Port</c> and the domain <c>Basil:Server:Domain</c>.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">The configuration to read.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddIrcListener(this IServiceCollection services,
		IConfiguration configuration)
	{
		services.AddOptions<IrcOptions>().Configure(irc =>
		{
			irc.Port = configuration.GetValue("Basil:Server:Irc:Port", irc.Port);
			irc.Domain = configuration.GetValue("Basil:Server:Domain", irc.Domain)!;
		});
		services.AddSingleton<IrcListener>();
		services.AddHostedService(sp => sp.GetRequiredService<IrcListener>());
		return services;
	}
}