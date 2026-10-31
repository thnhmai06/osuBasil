using Basil.Application.Users.Persistence;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

/// <summary>Answers whether one user blocks another where a room cares about it.</summary>
internal sealed class RoomBlocks
{
	private readonly IRelationshipRepository _relationships;

	/// <summary>Creates the source of the answers about blocks between users.</summary>
	/// <param name="relationships">The stored relationships between users.</param>
	internal RoomBlocks(IRelationshipRepository relationships)
	{
		_relationships = relationships;
	}

	/// <summary>Gets whether one user blocks another.</summary>
	/// <param name="blocker">The user whose blocks are checked.</param>
	/// <param name="who">The user who may be blocked.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when <paramref name="blocker" /> blocks <paramref name="who" />.</returns>
	/// <remarks>BasilBot blocks nobody.</remarks>
	internal async ValueTask<bool> BlocksAsync(UserRecord blocker, UserRecord who, CancellationToken cancellationToken)
	{
		if (blocker.IsBasilBot()) return false;

		var relationships = await _relationships.ListAsync(blocker, cancellationToken);
		return relationships.Any(relationship => relationship.Target.Equals(who) &&
		                                         relationship.Type == RelationshipType.Block);
	}
}