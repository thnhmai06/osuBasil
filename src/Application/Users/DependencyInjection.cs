using Basil.Application.Auth.Persistence;
using Basil.Application.Common.Events;
using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Users;

/// <summary>Registers the application behavior and runtime objects owned by Users and Connections.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the Users application services and runtime objects.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddUsersApplication(this IServiceCollection services)
	{
		services.AddSingleton<AccountService>();
		// The contexts and the registry refer to each other, so the contexts resolve the registry at first use.
		services.AddSingleton<UserContext>(sp => new UserContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<UserEvent>>(),
			new Lazy<UserRegistry>(sp.GetRequiredService<UserRegistry>),
			sp.GetRequiredService<IUserRepository>(),
			sp.GetRequiredService<IRestrictionRepository>(),
			sp.GetRequiredService<IModerationLogRepository>(),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<ILoginRepository>(),
			sp.GetRequiredService<ICredentialRepository>()));
		services.AddSingleton<ConnectionContext>(sp => new ConnectionContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<UserEvent>>(),
			new Lazy<UserRegistry>(sp.GetRequiredService<UserRegistry>),
			sp.GetRequiredService<IRelationshipRepository>()));
		services.AddSingleton<UserRegistry>(sp => new UserRegistry(
			sp.GetRequiredService<AccountService>(),
			sp.GetRequiredService<UserContext>(),
			sp.GetRequiredService<ConnectionContext>()));
		return services;
	}
}