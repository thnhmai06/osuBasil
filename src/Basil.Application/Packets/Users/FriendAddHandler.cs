using Basil.Application.Abstractions.Social;
using Basil.Application.Sessions;
using Basil.Domain.Social;
using Basil.Protocol.Packets;

namespace Basil.Application.Packets.Users;

/// <summary>
///     Handles the FriendAdd packet, which adds another user to the userSession's friends' list.
/// </summary>
/// <remarks>
///     The packet body is the target user's id as a 32-bit integer. Adding yourself is ignored. If
///     no relationship between the two users already exists, a
///     <see cref="RelationshipType.Friend" /> relationship is created through
///     <see cref="IRelationshipRepository" />.
/// </remarks>
public sealed class FriendAddHandler(IRelationshipRepository relationships) : IPacketHandler
{
	public ClientPackets PacketId => ClientPackets.FriendAdd;

	public bool AllowedWhenRestricted => false;

	public async Task HandleAsync(GameSession gameSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var targetId = reader.ReadI32();
		if (targetId == gameSession.Id) return;

		if (await relationships.FetchOneAsync(gameSession.Id, targetId, cancellationToken) is null)
			await relationships.CreateAsync(gameSession.Id, targetId, RelationshipType.Friend, cancellationToken);
	}
}