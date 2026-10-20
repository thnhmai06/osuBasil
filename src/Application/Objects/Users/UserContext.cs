using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Events;
using Basil.Application.Storage.Auth;
using Basil.Application.Storage.Users;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Users;

/// <summary>The ports through which an account performs its behaviour.</summary>
/// <param name="Clock">The clock for evaluating restrictions.</param>
/// <param name="Events">The publisher for account events.</param>
/// <param name="Registry">The sessions of online users.</param>
/// <param name="Users">The stored account records.</param>
/// <param name="Restrictions">The stored restrictions.</param>
/// <param name="ModerationLog">The recorded moderation history.</param>
/// <param name="Relationships">The friends and blocks users set toward each other.</param>
/// <param name="Logins">The recorded logins.</param>
/// <param name="Credentials">The stored passwords.</param>
/// <param name="Sessions">The operations on online sessions.</param>
internal sealed record UserContext(
	TimeProvider Clock,
	IEventPublisher<UserEvent> Events,
	IUserRegistry Registry,
	IUserRepository Users,
	IRestrictionRepository Restrictions,
	IModerationLogRepository ModerationLog,
	IRelationshipRepository Relationships,
	ILoginRepository Logins,
	ICredentialRepository Credentials,
	IUserSessionsPort Sessions);

/// <summary>What an account asks of the sessions that hold it online.</summary>
internal interface IUserSessionsPort
{
	/// <summary>Closes every open connection of an account.</summary>
	/// <param name="user">The account whose connections are closed.</param>
	/// <param name="reason">Why the connections are closed.</param>
	void CloseAll(UserRecord user, ConnectionCloseReason reason);

	/// <summary>Closes connections of kinds the account may no longer use.</summary>
	/// <param name="session">The online session whose connections are checked.</param>
	void CloseDisallowed(UserSession session);
}