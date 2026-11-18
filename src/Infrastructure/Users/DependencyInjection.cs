using Basil.Application.Users.Persistence;
using Basil.Application.Users.Ports;
using Basil.Application.Users.Storage;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Users.Logic;
using Basil.Infrastructure.Users.Persistence;
using Basil.Infrastructure.Users.Storage;
using Basil.Infrastructure.Users.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Users;

public static class DependencyInjection
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddUsersInfrastructure()
		{
			services.AddSingleton<IModuleMigration, ModuleMigration<UsersDbContext>>();
			services.AddSingleton<IUserRepository, EfUserRepository>();
			services.AddSingleton<IRelationshipRepository, EfRelationshipRepository>();
			services.AddSingleton<IModerationLogRepository, EfModerationLogRepository>();
			services.AddSingleton<IUserAvatarStorage, FileUserAvatarStorage>();
			services.AddUsersLogic();
			return services;
		}

		public IServiceCollection AddUsersLogic()
		{
			services.AddSingleton<IAccountRules, AccountRules>();
			return services;
		}

		public IServiceCollection AddUsersBackgroundWork()
		{
			services.AddHostedService<IdleConnectionSweeper>();
			services.AddHostedService<SessionRetentionSweeper>();
			return services;
		}
	}
}