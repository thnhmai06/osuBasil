using Basil.Application.Sessions.Objects.Events;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Sessions.Registry;
using Basil.Application.Shared.Events;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;

namespace Basil.Application.Sessions.Objects;

public sealed record SessionContext(
	TimeProvider Clock,
	IEventPublisher<SessionEvent> Events,
	Lazy<SessionRegistry> RegistryOf,
	IUserRepository Users,
	RelationshipRegistry Relationships,
	IModerationLogRepository ModerationLog,
	ISessionRepository Sessions,
	ICredentialRepository Credentials,
	ISettingsRepository Settings)
{
	public SessionRegistry Registry => RegistryOf.Value;
}