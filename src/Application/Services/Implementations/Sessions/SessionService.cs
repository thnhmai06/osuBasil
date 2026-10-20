using System.Threading.Channels;
using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Implementations.Sessions;

/// <summary>
///     Opens and closes the connections of online users and changes what they share; connections and presence are its
///     parts.
/// </summary>
internal sealed class SessionService(
	SessionConnectionsService connections,
	SessionEventStream events) : ISessionService
{
	/// <inheritdoc />
	public ChannelReader<UserEvent> Events => events.Reader;

	/// <inheritdoc />
	public ISessionConnectionsService Connections => connections;
}
