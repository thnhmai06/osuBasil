using Basil.Application.Auth.Persistence;
using Basil.Infrastructure.Auth.Persistence;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Auth;

/// <summary>Registers the persistence implementations owned by Auth.</summary>
public static class DependencyInjection
{
	/// <summary>Adds Auth persistence implementations.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<AuthDbContext>>();
		services.AddSingleton<ICredentialRepository, PostgresCredentialRepository>();
		services.AddSingleton<ILoginRepository, PostgresLoginRepository>();
		return services;
	}
}