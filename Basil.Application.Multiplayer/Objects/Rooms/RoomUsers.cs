using Basil.Application.Users;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

internal sealed class RoomUsers
{
	private readonly IRelationshipRepository _relationships;
	private readonly IUserRepository _users;

	internal RoomUsers(IRelationshipRepository relationships, IUserRepository users)
	{
		_relationships = relationships;
		_users = users;
	}

	internal async ValueTask<UserRecord?> FindAsync(UserReference reference, CancellationToken ct = default)
	{
		return reference.Id is { } id
			? await _users.GetAsync(id, ct)
			: reference.Name is { } name
				? await _users.GetByNameAsync(name, ct)
				: null;
	}

	internal async ValueTask<bool> BlocksAsync(UserRecord blocker, UserRecord who, CancellationToken ct = default)
	{
		if (blocker.Id == BasilBot.Id) return false;

		var relationships = await _relationships.ListAsync(blocker, ct);
		return relationships.Any(relationship => relationship.Target.Equals(who) &&
		                                         relationship.Type == RelationshipType.Block);
	}
}