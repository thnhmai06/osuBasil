using Basil.Application.Anticheat.Services;
using Basil.Infrastructure.Anticheat.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Anticheat;

/// <summary>Registers the service implementations owned by Anticheat.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the anticheat reporter.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddAnticheatInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IAnticheatReporter, AnticheatReporter>();
		return services;
	}
}