using Basil.Infrastructure.Common.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Infrastructure.Postgres;

/// <summary>Registers PostgreSQL as the server's database engine.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the PostgreSQL provider that the shared database and the module migrations use.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddPostgresInfrastructure(this IServiceCollection services)
	{
		services.TryAddSingleton<IDatabaseProvider, PostgresDatabaseProvider>();
		return services;
	}
}
