using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;

namespace Basil.Application.Users.Objects.Connections;

public sealed record ConnectionContext(
	TimeProvider Clock,
	IEventPublisher<UserEvent> Events,
	Lazy<UserRegistry> RegistryOf,
	IRelationshipRepository Relationships)
{
	public UserRegistry Registry => RegistryOf.Value;
}