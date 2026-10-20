using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Storage.Contracts.Users;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>The ports through which a session and its connections perform their behaviour.</summary>
/// <param name="Clock">The clock for checking current time.</param>
/// <param name="Events">The event sink for emitting user events.</param>
/// <param name="Registry">The sessions of online users.</param>
/// <param name="Relationships">The relationships users set toward each other.</param>
/// <param name="Chat">The runtime dependencies for the session's channels.</param>
internal sealed record SessionContext(
	TimeProvider Clock,
	ISessionEventSink Events,
	IUserRegistry Registry,
	IRelationshipRepository Relationships,
	ChannelContext Chat);

/// <summary>Emits user events to the session event stream.</summary>
internal interface ISessionEventSink
{
	/// <summary>Emits a user event.</summary>
	/// <param name="event">The event to emit.</param>
	void Emit(UserEvent @event);
}