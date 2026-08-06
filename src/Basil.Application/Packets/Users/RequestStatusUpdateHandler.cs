using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Users;

/// <summary>
///     Handles the RequestStatusUpdate packet, which asks the server to resend the userSession's own
///     current stats.
/// </summary>
/// <remarks>
///     The client sends this when it wants a fresh copy of its own user-stats packet. The handler
///     rebuilds the stats' packet with <see cref="PacketBuilders.BuildUserStats" /> and enqueues it
///     on the userSession's outgoing queue.
/// </remarks>
public sealed class RequestStatusUpdateHandler : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.RequestStatusUpdate;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		gameSession.Enqueue(PacketBuilders.BuildUserStats(gameSession));
		return Task.CompletedTask;
	}
}