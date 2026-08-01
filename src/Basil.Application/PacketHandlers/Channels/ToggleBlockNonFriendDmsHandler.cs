using Basil.Application.PacketHandlers.Core;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Channels;

/// <summary>
///     Handles the <see cref="ClientPackets.ToggleBlockNonFriendDms" /> packet, which toggles whether
///     the player accepts private messages from users who are not friends. Reads a 32-bit integer and
///     sets <see cref="PlayerSession.PmPrivate" /> to match, so a value of 1 enables the block.
/// </summary>
public sealed class ToggleBlockNonFriendDmsHandler : IBanchoPacketHandler
{
	public ClientPackets PacketId => ClientPackets.ToggleBlockNonFriendDms;

	public bool AllowedWhenRestricted => true;

	public Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		player.PmPrivate = reader.ReadI32() == 1;
		return Task.CompletedTask;
	}
}