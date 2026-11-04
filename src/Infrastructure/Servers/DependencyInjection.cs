using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Servers;

/// <summary>Registers what the Servers module implements.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the implementations owned by Servers; its handlers are registered with the other Mediator handlers.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddServersInfrastructure(this IServiceCollection services)
	{
		return services;
	}
}
