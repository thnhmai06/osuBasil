using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Channels;

/// <summary>
///     Handles the <see cref="ClientPackets.ToggleBlockNonFriendDms" /> packet, which toggles whether
///     the userSession accepts private messages from users who are not friends. Reads a 32-bit integer and
///     sets <see cref="UserSession.PmPrivate" /> to match, so a value of 1 enables the block.
/// </summary>
public sealed class ToggleBlockNonFriendDmsHandler : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.ToggleBlockNonFriendDms;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		gameSession.PmPrivate = reader.ReadI32() == 1;
		return Task.CompletedTask;
	}
}