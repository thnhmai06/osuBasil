using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Objects.Events;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Sessions.Ports;
using Basil.Application.Sessions.Registry;
using Basil.Application.Shared.Events;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Sessions;

public static class DependencyInjection
{
	public static IServiceCollection AddSessionsApplication(this IServiceCollection services)
	{
		// The context and the registry refer to each other, so the context resolves the registry at first use.
		services.AddSingleton<SessionContext>(sp => new SessionContext(
			sp.GetRequiredService<TimeProvider>(),
			sp.GetRequiredService<IEventPublisher<SessionEvent>>(),
			new Lazy<SessionRegistry>(sp.GetRequiredService<SessionRegistry>),
			sp.GetRequiredService<IUserRepository>(),
			sp.GetRequiredService<IRelationshipRepository>(),
			sp.GetRequiredService<IModerationLogRepository>(),
			sp.GetRequiredService<ISessionRepository>(),
			sp.GetRequiredService<ICredentialRepository>(),
			sp.GetRequiredService<ISettingsRepository>()));
		services.AddSingleton<ActivityLog>();
		services.AddSingleton(sp => new AccountRegistration(sp.GetRequiredService<IAccountRules>()));
		services.AddSingleton(sp => new SessionMaintenance(sp.GetRequiredService<SessionContext>(),
			sp.GetRequiredService<ActivityLog>()));
		services.AddSingleton(sp => new BotSessions(sp.GetRequiredService<SessionRegistry>(),
			sp.GetRequiredService<SessionContext>(), sp.GetRequiredService<UserRegistry>()));
		services.AddSingleton<SessionRegistry>(sp => new SessionRegistry(
			sp.GetRequiredService<IAccountRules>(),
			sp.GetRequiredService<AccountRegistration>(),
			sp.GetRequiredService<ActivityLog>(),
			sp.GetRequiredService<SessionContext>(),
			sp.GetRequiredService<UserRegistry>()));
		return services;
	}
}