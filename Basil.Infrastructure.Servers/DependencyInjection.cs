using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Servers;

public static class DependencyInjection
{
	public static IServiceCollection AddServersInfrastructure(this IServiceCollection services)
	{
		return services;
	}
}