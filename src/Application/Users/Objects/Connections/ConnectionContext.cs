using Basil.Application.Common.Events;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;

namespace Basil.Application.Users.Objects.Connections;

/// <summary>The ports through which the connections perform their behaviour.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The publisher for user events.</param>
/// <param name="RegistryOf">Resolves the sessions of online users, once the registry exists.</param>
/// <param name="Relationships">The relationships users set toward each other.</param>
public sealed record ConnectionContext(
	TimeProvider Clock,
	IEventPublisher<UserEvent> Events,
	Lazy<UserRegistry> RegistryOf,
	IRelationshipRepository Relationships)
{
	/// <summary>Gets the sessions of online users.</summary>
	public UserRegistry Registry => RegistryOf.Value;
}