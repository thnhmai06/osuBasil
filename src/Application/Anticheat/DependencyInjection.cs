using Basil.Application.Anticheat.Objects.Events;
using Basil.Application.Anticheat.Services;
using Basil.Application.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Anticheat;

/// <summary>Registers the anticheat services and event publishers.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the anticheat reporter.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddAnticheatApplication(this IServiceCollection services)
	{
		services.AddSingleton<IAnticheatReporter>(sp => new AnticheatReporter(
			sp.GetRequiredService<IEventPublisher<AnticheatEvent>>()));
		return services;
	}
}