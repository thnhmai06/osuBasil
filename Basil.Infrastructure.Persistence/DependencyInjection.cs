using Basil.Infrastructure.Persistence.Providers;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Infrastructure.Persistence;

public static class DependencyInjection
{
	public static IServiceCollection AddPersistenceInfrastructure(this IServiceCollection services)
	{
		services.TryAddSingleton<IDatabaseProvider, PostgresDatabaseProvider>();
		return services;
	}
}