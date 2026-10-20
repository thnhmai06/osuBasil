using Basil.Application.Objects.Sessions;

namespace Basil.Application.Services.Sessions;

/// <summary>
///     Opens and closes the connections of online users and changes what they share; connections and presence are its
///     parts.
/// </summary>
internal sealed class SessionService(
	SessionConnectionsService connections) : ISessionService
{
	/// <inheritdoc />
	public ISessionConnectionsService Connections => connections;
}