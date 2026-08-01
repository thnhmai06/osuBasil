using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>
///     Handles the RequestStatusUpdate packet, which asks the server to resend the player's own
///     current stats.
/// </summary>
/// <remarks>
///     The client sends this when it wants a fresh copy of its own user-stats packet. The handler
///     rebuilds the stats packet with <see cref="PacketBuilders.BuildUserStats" /> and enqueues it
///     on the player's outgoing queue.
/// </remarks>
public sealed class RequestStatusUpdateHandler : IBanchoPacketHandler
{
	/// <summary>The <see cref="ClientPackets.RequestStatusUpdate" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.RequestStatusUpdate;

	/// <summary>Restricted players may request their own stats, so this handler is always available.</summary>
	public bool AllowedWhenRestricted => true;

	/// <summary>Enqueues a rebuilt user-stats packet for the requesting player.</summary>
	/// <param name="player">The player session that requested its stats.</param>
	/// <param name="reader">The packet reader positioned at the RequestStatusUpdate body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task.</returns>
	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		player.Enqueue(PacketBuilders.BuildUserStats(player));
		return Task.CompletedTask;
	}
}