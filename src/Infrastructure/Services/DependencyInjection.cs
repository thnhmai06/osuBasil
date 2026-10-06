using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Services;

/// <summary>Registers the capability ports that reach outside the process.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the capability ports the server needs.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
	{
		// Capability ports are registered by the phases that add them.
		return services;
	}
}
