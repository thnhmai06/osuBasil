using Basil.Application.Sessions.Persistence;
using Basil.Infrastructure.Sessions.Persistence;
using Basil.Infrastructure.Sessions.Workers;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Sessions;

public static class DependencyInjection
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddSessionsInfrastructure()
		{
			services.AddSingleton<IModuleMigration, ModuleMigration<SessionsDbContext>>();
			services.AddSingleton<ICredentialRepository, EfCredentialRepository>();
			services.AddSingleton<ISessionRepository, EfSessionRepository>();
			return services;
		}

		public IServiceCollection AddSessionsBackgroundWork()
		{
			services.AddHostedService<SessionSweeper>();
			services.AddHostedService<SessionRetentionSweeper>();
			return services;
		}
	}
}