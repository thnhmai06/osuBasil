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
	public ClientPackets PacketId => ClientPackets.Ping;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(GameSession gameSession, PacketReader reader, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}
}