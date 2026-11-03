using Basil.Application.Authentication.Persistence;
using Basil.Infrastructure.Authentication.Persistence;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Authentication;

/// <summary>Registers the persistence implementations owned by Auth.</summary>
public static class DependencyInjection
{
	/// <summary>Adds Auth persistence implementations.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<AuthenticationDbContext>>();
		services.AddSingleton<ICredentialRepository, PostgresCredentialRepository>();
		services.AddSingleton<ISessionRepository, PostgresSessionRepository>();
		return services;
	}
}