using Basil.Application.Users.Objects;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Objects.Rooms;

/// <summary>Answers what a room asks about users: who a reference names and whether one user blocks another.</summary>
internal sealed class RoomUsers
{
	private readonly IRelationshipRepository _relationships;
	private readonly IUserRepository _users;

	/// <summary>Creates the source of the answers about users.</summary>
	/// <param name="relationships">The stored relationships between users.</param>
	/// <param name="users">The stored users.</param>
	internal RoomUsers(IRelationshipRepository relationships, IUserRepository users)
	{
		_relationships = relationships;
		_users = users;
	}

	/// <summary>Gets the account a reference names.</summary>
	/// <param name="reference">The id or name the caller has.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The account, or <see langword="null" /> when no account has that id or name.</returns>
	internal async ValueTask<UserRecord?> FindAsync(UserReference reference, CancellationToken ct = default)
	{
		return reference.Id is { } id
			? await _users.GetAsync(id, ct)
			: reference.Name is { } name
				? await _users.GetByNameAsync(name, ct)
				: null;
	}

	/// <summary>Gets whether one user blocks another.</summary>
	/// <param name="blocker">The user whose blocks are checked.</param>
	/// <param name="who">The user who may be blocked.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when <paramref name="blocker" /> blocks <paramref name="who" />.</returns>
	/// <remarks>BasilBot blocks nobody.</remarks>
	internal async ValueTask<bool> BlocksAsync(UserRecord blocker, UserRecord who, CancellationToken ct = default)
	{
		if (blocker.IsFriendOfEveryone()) return false;

		var relationships = await _relationships.ListAsync(blocker, ct);
		return relationships.Any(relationship => relationship.Target.Equals(who) &&
		                                         relationship.Type == RelationshipType.Block);
	}
}