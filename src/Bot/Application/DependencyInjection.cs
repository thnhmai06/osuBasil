using Microsoft.Extensions.DependencyInjection;

namespace Basil.Bot.Application;

/// <summary>Registers the BasilBot application's services.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the services BasilBot needs.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddBotApplication(this IServiceCollection services)
	{
		return services;
	}
}
