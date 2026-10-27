using Basil.Infrastructure.Common.Postgres;
using Basil.Application.Users.Persistence;
using Basil.Infrastructure.Users.Persistence;
using Basil.Infrastructure.Users.Runtime;
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
		services.AddSingleton<IUserRepository, PostgresUserRepository>();
		services.AddSingleton<IRestrictionRepository, PostgresRestrictionRepository>();
		services.AddSingleton<IRelationshipRepository, PostgresRelationshipRepository>();
		services.AddSingleton<IModerationLogRepository, PostgresModerationLogRepository>();
		services.AddSingleton<IUserAvatarStorage, FileUserAvatarStorage>();
		return services;
	}

	/// <summary>Adds the background work of Users: closing connections that stay idle.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddUsersBackgroundWork(this IServiceCollection services)
	{
		services.AddHostedService<IdleConnectionSweeper>();
		return services;
	}
}