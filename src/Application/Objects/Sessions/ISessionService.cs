namespace Basil.Application.Objects.Sessions;

/// <summary>
///     Opens and closes the connections of online users.
/// </summary>
public interface ISessionService
{
	/// <summary>Connections: closing them, keeping them alive, acting for another user.</summary>
	ISessionConnectionsService Connections { get; }
}