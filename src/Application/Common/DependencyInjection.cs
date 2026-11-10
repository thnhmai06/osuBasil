using Basil.Application.Common.Events;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Application.Common;

/// <summary>Registers the application services shared by every module.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the shared application services.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddCommonApplication(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<EventQueue>();
		services.TryAddSingleton(sp => new EventPublisher(
			new Lazy<IPublisher>(sp.GetRequiredService<IPublisher>),
			sp.GetRequiredService<TimeProvider>()));
		return services;
	}
}