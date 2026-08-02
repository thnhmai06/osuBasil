using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Users;

/// <summary>
///     Handles the Ping packet, which the client sends to keep the connection alive.
/// </summary>
/// <remarks>
///     The packet carries no meaningful payload and requires no response, so handling it is a no-op.
///     Keeping the session marked as alive is handled by the transport layer when it reads the
///     request, not by this handler.
/// </remarks>
public sealed class PingHandler : IPacketHandler
{
	/// <summary>The <see cref="ClientPackets.Ping" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.Ping;

	/// <summary>Restricted players may ping, so this handler is always available.</summary>
	public bool AllowedWhenRestricted => true;

	/// <summary>Does nothing.</summary>
	/// <param name="userSession">The userSession session that sent the ping.</param>
	/// <param name="reader">The packet reader positioned at the Ping body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task.</returns>
	public Task HandleAsync(UserSession userSession, PacketReader reader, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}
}