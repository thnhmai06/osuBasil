using Basil.Application.Users.Persistence;
using Basil.Application.Users.Ports;
using Basil.Application.Users.Storage;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Users.Logic;
using Basil.Infrastructure.Users.Persistence;
using Basil.Infrastructure.Users.Storage;
using Basil.Infrastructure.Users.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Users;

/// <summary>Registers the persistence implementations and background work owned by Users.</summary>
public static class DependencyInjection
{
	/// <summary>Adds Users persistence implementations.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
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

	/// <summary>Adds the account rules that the user registry applies at registration and sign-in.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddUsersLogic(this IServiceCollection services)
	{
		services.AddSingleton<IAccountRules, AccountRules>();
		return services;
	}

	/// <summary>Adds the background work of Users: closing connections that stay idle and deleting old ended sessions.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddUsersBackgroundWork(this IServiceCollection services)
	{
		services.AddHostedService<IdleConnectionSweeper>();
		services.AddHostedService<SessionRetentionSweeper>();
		return services;
	}
}