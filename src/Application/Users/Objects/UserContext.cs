using Basil.Application.Authentication.Persistence;
using Basil.Application.Content.Ports;
using Basil.Application.Shared.Events;
using Basil.Application.Shared.Settings;
using Basil.Application.Shared.Storage;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Application.Users.Storage;

namespace Basil.Application.Users.Objects;

internal sealed record UserContext(
	TimeProvider Clock,
	IEventPublisher<UserEvent> Events,
	Lazy<UserRegistry> RegistryOf,
	IUserRepository Users,
	IModerationLogRepository ModerationLog,
	IRelationshipRepository Relationships,
	ISessionRepository Sessions,
	IConnectionRepository Connections,
	ICredentialRepository Credentials,
	IUserAvatarStorage Avatars,
	IScreenshotInspector Inspector,
	IUploadSpool Spool,
	ISettingsRepository Settings)
{
	public UserRegistry Registry => RegistryOf.Value;
}