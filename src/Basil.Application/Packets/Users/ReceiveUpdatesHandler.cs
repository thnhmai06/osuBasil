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
///     accepted value is stored on <see cref="PresenceFilter" />.
/// </remarks>
public sealed class ReceiveUpdatesHandler : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.ReceiveUpdates;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(GameSession gameSession, PacketReader reader, CancellationToken cancellationToken = default)
	{
		var value = reader.ReadI32();
		if (value is < 0 or >= 3) return Task.CompletedTask;

		gameSession.PresenceFilter = (PresenceFilter)value;
		return Task.CompletedTask;
	}
}