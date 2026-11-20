using Basil.Application.Sessions.Persistence;
using Basil.Infrastructure.Sessions.Persistence;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Sessions;

public static class DependencyInjection
{
	public static IServiceCollection AddSessionsInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<SessionsDbContext>>();
		services.AddSingleton<ICredentialRepository, EfCredentialRepository>();
		services.AddSingleton<ISessionRepository, EfSessionRepository>();
		return services;
	}
}