using Basil.Application.Shared.Events;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Application.Shared;

public static class DependencyInjection
{
	public static IServiceCollection AddSharedApplication(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<EventQueue>();
		services.TryAddSingleton(sp => new EventPublisher(
			new Lazy<IPublisher>(sp.GetRequiredService<IPublisher>),
			sp.GetRequiredService<TimeProvider>()));
		return services;
	}
}