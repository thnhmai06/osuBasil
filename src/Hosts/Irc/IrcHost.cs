using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Host.Irc;

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
		return services;
	}

	/// <summary>Adds the socket listener, which is what makes the server answer on the IRC port.</summary>
	/// <param name="services">The service collection.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddIrcListener(this IServiceCollection services)
	{
		services.AddSingleton<IrcListener>();
		services.AddHostedService(sp => sp.GetRequiredService<IrcListener>());
		return services;
	}
}
