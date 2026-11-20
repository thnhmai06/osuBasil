using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Application.Shared;

public static class DependencyInjection
{
	public static IServiceCollection AddSharedApplication(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);
		return services;
	}
}