using Basil.Application.Persistence.Users;
using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Users;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Sessions;

/// <summary>The ports through which a session and its connections perform their behaviour.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The publisher for user events.</param>
/// <param name="RegistryOf">Resolves the sessions of online users, once the registry exists.</param>
/// <param name="Relationships">The relationships users set toward each other.</param>
internal sealed record SessionContext(
	TimeProvider Clock,
	IEventPublisher<UserEvent> Events,
	Lazy<UserRegistry> RegistryOf,
	IRelationshipRepository Relationships)
{
	/// <summary>Gets the sessions of online users.</summary>
	public UserRegistry Registry => RegistryOf.Value;
}
