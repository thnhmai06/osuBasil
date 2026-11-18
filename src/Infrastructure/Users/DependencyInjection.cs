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
	public static IServiceCollection AddUsersInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<UsersDbContext>>();
		services.AddSingleton<IUserRepository, EfUserRepository>();
		services.AddSingleton<IRelationshipRepository, EfRelationshipRepository>();
		services.AddSingleton<IModerationLogRepository, EfModerationLogRepository>();
		services.AddSingleton<IUserAvatarStorage, FileUserAvatarStorage>();
		services.AddUsersLogic();
		return services;
	}

	public static IServiceCollection AddUsersLogic(this IServiceCollection services)
	{
		services.AddSingleton<IAccountRules, AccountRules>();
		return services;
	}

	public static IServiceCollection AddUsersBackgroundWork(this IServiceCollection services)
	{
		services.AddHostedService<IdleConnectionSweeper>();
		services.AddHostedService<SessionRetentionSweeper>();
		return services;
	}
}