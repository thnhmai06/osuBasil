using Basil.Application.Authentication.Persistence;
using Basil.Infrastructure.Authentication.Persistence;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Authentication;

public static class DependencyInjection
{
	public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<AuthenticationDbContext>>();
		services.AddSingleton<ICredentialRepository, EfCredentialRepository>();
		services.AddSingleton<ISessionRepository, EfSessionRepository>();
		return services;
	}
}