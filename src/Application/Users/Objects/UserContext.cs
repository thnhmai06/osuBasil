using Basil.Application.Auth.Persistence;
using Basil.Application.Common.Events;
using Basil.Application.Content.Capabilities;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Application.Users.Storage;

namespace Basil.Application.Users.Objects;

/// <summary>The ports through which an account performs its behaviour.</summary>
/// <param name="Clock">The clock for evaluating restrictions.</param>
/// <param name="Events">The publisher for account events.</param>
/// <param name="RegistryOf">Resolves the users who are online, once the registry exists.</param>
/// <param name="Users">The stored account records.</param>
/// <param name="Restrictions">The stored restrictions.</param>
/// <param name="ModerationLog">The recorded moderation history.</param>
/// <param name="Relationships">The friends and blocks users set toward each other.</param>
/// <param name="Sessions">The recorded sessions.</param>
/// <param name="Credentials">The stored passwords.</param>
/// <param name="Avatars">The stored avatar images.</param>
/// <param name="Inspector">Identifies the format and dimensions of uploaded pictures.</param>
/// <param name="Settings">The server settings that decide token lifetimes and locks.</param>
internal sealed record UserContext(
	TimeProvider Clock,
	IEventPublisher<UserEvent> Events,
	Lazy<UserRegistry> RegistryOf,
	IUserRepository Users,
	IRestrictionRepository Restrictions,
	IModerationLogRepository ModerationLog,
	IRelationshipRepository Relationships,
	ISessionRepository Sessions,
	ICredentialRepository Credentials,
	IUserAvatarStorage Avatars,
	IScreenshotInspector Inspector,
	ISettingsRepository Settings)
{
	/// <summary>Gets the users who are online.</summary>
	public UserRegistry Registry => RegistryOf.Value;
}