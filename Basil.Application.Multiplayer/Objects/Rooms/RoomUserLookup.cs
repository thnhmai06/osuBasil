using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

internal sealed class RoomUserLookup
{
	private readonly RelationshipRegistry _relationships;
	private readonly IUserRepository _users;

	internal RoomUserLookup(RelationshipRegistry relationships, IUserRepository users)
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

		return await _relationships.HasAsync(blocker, RelationshipType.Block, who.Id, ct);
	}
}