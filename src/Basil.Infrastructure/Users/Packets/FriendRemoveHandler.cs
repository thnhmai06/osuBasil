using Basil.Application.Sessions;
using Basil.Domain.Social;
using Basil.Infrastructure.Shared.Http.Bancho;
using Basil.Infrastructure.Shared.Sessions;
using Basil.Protocol.Packets;
using Basil.Application.Social;

namespace Basil.Infrastructure.Users.Packets;

/// <summary>
///     Handles the FriendRemove packet, which removes another user from the userSession's friends' list.
/// </summary>
/// <remarks>
///     The packet body is the target user's id as a 32-bit integer. The relationship is deleted only
///     when a <see cref="RelationshipType.Friend" /> relationship between the two users exists; a
///     missing or non-friend relationship, such as a block, is left untouched.
/// </remarks>
public sealed class FriendRemoveHandler(IRelationshipRepository relationships) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.FriendRemove;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var targetId = reader.ReadI32();
		var relationship = await relationships.FetchOneAsync(gameSession.Id, targetId, cancellationToken);
		if (relationship?.Type == RelationshipType.Friend)
			await relationships.DeleteAsync(gameSession.Id, targetId, cancellationToken);
	}
}