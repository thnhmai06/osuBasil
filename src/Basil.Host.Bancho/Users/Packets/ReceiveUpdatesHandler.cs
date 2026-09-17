using Basil.Application.Sessions;
using Basil.Domain.Social;
using Basil.Host.Bancho.Shared.Http;
using Basil.Protocol.Packets;

namespace Basil.Host.Bancho.Users.Packets;

/// <summary>
///     Handles the ReceiveUpdates packet, which changes how much presence information the client
///     wants to receive.
/// </summary>
/// <remarks>
///     The body carries a 32-bit integer that is interpreted as a <see cref="PresenceVisibility" />.
///     Values outside the valid 0..2 range are ignored, leaving the previous filter in place. The
///     accepted value is stored on <see cref="PresenceVisibility" />.
/// </remarks>
public sealed class ReceiveUpdatesHandler : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.ReceiveUpdates;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(GameSession gameSession, PacketReader reader, CancellationToken cancellationToken = default)
	{
		var value = reader.ReadI32();
		if (value is < 0 or >= 3) return Task.CompletedTask;

		gameSession.PresenceVisibility = (PresenceVisibility)value;
		return Task.CompletedTask;
	}
}