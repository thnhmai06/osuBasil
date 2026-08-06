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
	/// <summary>The <see cref="ClientPackets.RequestStatusUpdate" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.RequestStatusUpdate;

	/// <summary>Restricted players may request their own stats, so this handler is always available.</summary>
	public bool AllowedWhenRestricted => true;

	/// <summary>Enqueues a rebuilt user-stats packet for the requesting userSession.</summary>
	/// <param name="userSession">The userSession session that requested its stats.</param>
	/// <param name="reader">The packet reader positioned at the RequestStatusUpdate body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task.</returns>
	public Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		userSession.Enqueue(PacketBuilders.BuildUserStats(userSession));
		return Task.CompletedTask;
	}
}