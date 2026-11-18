using Basil.Gateway.RateLimiting;
using Basil.Host.Irc.Commands;
using Basil.Host.Irc.Session;
using Basil.Host.Shared.Documentation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Host.Irc.Transport;

public static class IrcHost
{
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