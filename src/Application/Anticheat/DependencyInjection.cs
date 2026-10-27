using Basil.Application.Anticheat.Runtime;
using Basil.Application.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Anticheat;

/// <summary>Registers the runtime objects owned by Anticheat.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the Anticheat runtime objects.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddAnticheatApplication(this IServiceCollection services)
	{
		services.AddSingleton<AnticheatReporter>(sp => new AnticheatReporter(
			sp.GetRequiredService<IEventPublisher<AnticheatEvent>>()));
		return services;
	}
}