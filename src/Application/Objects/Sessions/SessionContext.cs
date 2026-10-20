using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Users;
using Basil.Application.Storage.Users;

namespace Basil.Application.Objects.Sessions;

/// <summary>The ports through which a session and its connections perform their behaviour.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The publisher for user events.</param>
/// <param name="Registry">The sessions of online users.</param>
/// <param name="Relationships">The relationships users set toward each other.</param>
/// <param name="Chat">The runtime dependencies for the session's channels.</param>
internal sealed record SessionContext(
	TimeProvider Clock,
	IEventPublisher<UserEvent> Events,
	UserRegistry Registry,
	IRelationshipRepository Relationships,
	ChannelContext Chat);