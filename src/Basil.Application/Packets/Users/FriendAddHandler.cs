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
	/// <summary>The <see cref="ClientPackets.FriendAdd" /> packet type.</summary>
	public ClientPackets PacketId => ClientPackets.FriendAdd;

	/// <summary>Restricted players may not add friends, so this handler is unavailable to them.</summary>
	public bool AllowedWhenRestricted => false;

	/// <summary>Reads the target user id and adds them as a friend when the relationship does not exist.</summary>
	/// <param name="userSession">The userSession session that is adding a friend.</param>
	/// <param name="reader">The packet reader positioned at the FriendAdd body.</param>
	/// <param name="cancellationToken">The token used to cancel the operation.</param>
	/// <returns>A task that completes once the relationship lookup and optional creation finish.</returns>
	public async Task HandleAsync(GameSession userSession, PacketReader reader,
		CancellationToken cancellationToken = default)
	{
		var targetId = reader.ReadI32();
		if (targetId == userSession.Id) return;

		if (await relationships.FetchOneAsync(userSession.Id, targetId, cancellationToken) is null)
			await relationships.CreateAsync(userSession.Id, targetId, RelationshipType.Friend, cancellationToken);
	}
}