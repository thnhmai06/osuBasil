using Basil.Application.Users.Persistence;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

internal sealed class RoomUserLookup
{
	private readonly IRelationshipRepository _relationships;
	private readonly IUserRepository _users;

	internal RoomUserLookup(IRelationshipRepository relationships, IUserRepository users)
	{
		_relationships = relationships;
		_users = users;
	}

	internal async ValueTask<UserRecord?> FindAsync(int id, CancellationToken ct = default)
	{
		return await _users.GetAsync(id, ct);
	}

	internal async ValueTask<bool> BlocksAsync(UserRecord blocker, UserRecord who, CancellationToken ct = default)
	{
		if (blocker.Id == BasilBot.Id) return false;

		var relationships = await _relationships.ListAsync(blocker, ct);
		return relationships.Any(relationship => relationship.Target.Equals(who) &&
		                                         relationship.Type == RelationshipType.Block);
	}
}