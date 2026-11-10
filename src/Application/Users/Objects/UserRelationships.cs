using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

/// <summary>Owns the friends and blocks one account has set toward other users.</summary>
public sealed class UserRelationships
{
	private readonly User _user;

	/// <summary>Creates the relationship state for an account.</summary>
	/// <param name="user">The account that owns the relationships.</param>
	internal UserRelationships(User user)
	{
		_user = user;
	}

	/// <summary>Lists the relationships this account has set, of one kind.</summary>
	/// <param name="type">The kind of relationship.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The relationships of that kind.</returns>
	internal async Task<IReadOnlyList<Relationship>> ListAsync(RelationshipType type,
		CancellationToken ct = default)
	{
		var all = await _user.Context.Relationships.ListAsync(_user.Record, ct);
		return all.Where(relationship => relationship.Type == type).ToList();
	}

	/// <summary>Sets a relationship toward another user, replacing the one it had.</summary>
	/// <param name="target">The other user.</param>
	/// <param name="type">The kind of relationship.</param>
	internal Task SetAsync(UserRecord target, RelationshipType type)
	{
		return _user.Context.Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = _user.Record,
			Target = target,
			Type = type,
			CreatedAt = _user.Context.Clock.GetUtcNow()
		});
	}

	/// <summary>Removes a relationship of one kind toward another user, if it exists.</summary>
	/// <param name="target">The other user.</param>
	/// <param name="type">The kind of relationship.</param>
	internal async Task RemoveAsync(UserRecord target, RelationshipType type)
	{
		var existing = (await ListAsync(type)).FirstOrDefault(relationship =>
			relationship.Target.Equals(target));
		if (existing is not null)
			await _user.Context.Relationships.DeleteAsync(existing);
	}

	/// <summary>Adds another user as a friend.</summary>
	/// <param name="target">The other user.</param>
	/// <returns>The outcome of adding the friend.</returns>
	internal async Task<UserOutcome> AddFriendAsync(UserRecord target)
	{
		if (_user.Record.Equals(target)) return UserOutcome.Self;
		if (target.Value.DeletedAt is not null) return UserOutcome.Deleted;
		if (target.IsFriendOfEveryone()) return UserOutcome.Ok;

		await SetAsync(target, RelationshipType.Friend);
		return UserOutcome.Ok;
	}

	/// <summary>Removes another user as a friend.</summary>
	/// <param name="target">The other user.</param>
	/// <returns>The outcome of removing the friend.</returns>
	internal async Task<UserOutcome> RemoveFriendAsync(UserRecord target)
	{
		if (_user.Record.Equals(target)) return UserOutcome.Self;
		if (target.IsFriendOfEveryone()) return UserOutcome.SystemAccount;

		await RemoveAsync(target, RelationshipType.Friend);
		return UserOutcome.Ok;
	}

	/// <summary>Blocks another user.</summary>
	/// <param name="target">The other user.</param>
	/// <returns>The outcome of blocking the user.</returns>
	internal async Task<UserOutcome> BlockAsync(UserRecord target)
	{
		if (_user.Record.Equals(target)) return UserOutcome.Self;
		if (target.Value.DeletedAt is not null) return UserOutcome.Deleted;
		if (target.IsFriendOfEveryone()) return UserOutcome.SystemAccount;

		await SetAsync(target, RelationshipType.Block);
		return UserOutcome.Ok;
	}

	/// <summary>Removes a block on another user.</summary>
	/// <param name="target">The other user.</param>
	/// <returns>The outcome of removing the block.</returns>
	internal async Task<UserOutcome> UnblockAsync(UserRecord target)
	{
		if (_user.Record.Equals(target)) return UserOutcome.Self;
		if (target.IsFriendOfEveryone()) return UserOutcome.Ok;

		await RemoveAsync(target, RelationshipType.Block);
		return UserOutcome.Ok;
	}

	/// <summary>Lists the users this account has set a relationship toward, of one kind.</summary>
	/// <param name="type">The kind of relationship.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The users the relationships name.</returns>
	internal async Task<IReadOnlyList<UserRecord>> TargetsAsync(RelationshipType type,
		CancellationToken ct)
	{
		var relationships = await ListAsync(type, ct);
		return relationships.Select(relationship => relationship.Target).ToList();
	}
}