using Basil.Application.Runtime.Chat;
using Basil.Application.Runtime.Sessions;
using Basil.Application.Runtime.Events;
using Basil.Application.Persistence.Auth;
using Basil.Application.Persistence.Users;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Users;

/// <summary>The ports through which an account performs its behaviour.</summary>
/// <param name="Clock">The clock for evaluating restrictions.</param>
/// <param name="Events">The publisher for account events.</param>
/// <param name="RegistryOf">Resolves the users who are online, once the registry exists.</param>
/// <param name="Users">The stored account records.</param>
/// <param name="Restrictions">The stored restrictions.</param>
/// <param name="ModerationLog">The recorded moderation history.</param>
/// <param name="Relationships">The friends and blocks users set toward each other.</param>
/// <param name="Logins">The recorded logins.</param>
/// <param name="Credentials">The stored passwords.</param>
/// <param name="Chat">The runtime dependencies for the user's private-message channel.</param>
internal sealed record UserContext(
	TimeProvider Clock,
	IEventPublisher<UserEvent> Events,
	Lazy<UserRegistry> RegistryOf,
	IUserRepository Users,
	IRestrictionRepository Restrictions,
	IModerationLogRepository ModerationLog,
	IRelationshipRepository Relationships,
	ILoginRepository Logins,
	ICredentialRepository Credentials,
	ChannelContext Chat)
{
	/// <summary>Gets the users who are online.</summary>
	public UserRegistry Registry => RegistryOf.Value;
}