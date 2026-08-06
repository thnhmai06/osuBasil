using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Users;

/// <summary>
///     Handles the ReceiveUpdates packet, which changes how much presence information the client
///     wants to receive.
/// </summary>
/// <remarks>
///     The body carries a 32-bit integer that is interpreted as a <see cref="PresenceFilter" />.
///     Values outside the valid 0..2 range are ignored, leaving the previous filter in place. The
///     accepted value is stored on <see cref="UserSession.PresenceFilter" />.
/// </remarks>
public sealed class ReceiveUpdatesHandler : IPacketHandler
{
	/// <summary>The <see cref="ClientPackets.ReceiveUpdates" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.ReceiveUpdates;

	/// <summary>Restricted players may update their presence filter, so this handler is always available.</summary>
	public bool AllowedWhenRestricted => true;

	/// <summary>Reads the filter value and stores it on the userSession's session when it is valid.</summary>
	/// <param name="gameSession">The userSession session whose presence filter is being updated.</param>
	/// <param name="reader">The packet reader positioned at the ReceiveUpdates body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A completed task.</returns>
	public Task HandleAsync(GameSession gameSession, PacketReader reader, CancellationToken cancellationToken = default)
	{
		var value = reader.ReadI32();
		if (value is < 0 or >= 3) return Task.CompletedTask;

		gameSession.PresenceFilter = (PresenceFilter)value;
		return Task.CompletedTask;
	}
}