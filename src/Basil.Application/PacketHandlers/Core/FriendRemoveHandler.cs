using Basil.Application.Abstractions.Social;
using Basil.Application.Sessions;
using Basil.Protocol.Packets;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>
///     Handles the FriendRemove packet, which removes another user from the player's friends list.
/// </summary>
/// <remarks>
///     The packet body is the target user's id as a 32-bit integer. The relationship is deleted only
///     when a <see cref="RelationshipType.Friend" /> relationship between the two users exists; a
///     missing or non-friend relationship, such as a block, is left untouched.
/// </remarks>
public sealed class FriendRemoveHandler(IRelationshipRepository relationships) : IBanchoPacketHandler
{
	/// <summary>The <see cref="ClientPackets.FriendRemove" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.FriendRemove;

	/// <summary>Restricted players may not remove friends, so this handler is unavailable to them.</summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Reads the target user id and deletes the friend relationship when one exists.</summary>
	/// <param name="player">The player session that is removing a friend.</param>
	/// <param name="reader">The packet reader positioned at the FriendRemove body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A task that completes once the relationship lookup and optional deletion finish.</returns>
	public async Task HandleAsync(PlayerSession player, BanchoPacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var targetId = reader.ReadI32();
		var relationship = await relationships.FetchOneAsync(player.Id, targetId);
		if (relationship?.Type == RelationshipType.Friend)
			await relationships.DeleteAsync(player.Id, targetId);
	}
}