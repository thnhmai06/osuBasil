using Basil.Application.Services.Contracts.Events;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>
///     Opens and closes the connections of online users.
/// </summary>
public interface ISessionService : IEventPublisher<UserEvent>
{
	/// <summary>Connections: closing them, keeping them alive, acting for another user.</summary>
	ISessionConnectionsService Connections { get; }
}
