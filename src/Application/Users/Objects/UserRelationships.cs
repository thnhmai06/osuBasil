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
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The relationships of that kind.</returns>
	internal async Task<IReadOnlyList<Relationship>> ListAsync(RelationshipType type,
		CancellationToken cancellationToken)
	{
		var all = await _user.Context.Relationships.ListAsync(_user.Record, cancellationToken);
		return all.Where(relationship => relationship.Type == type).ToList();
	}

	/// <summary>Sets a relationship toward another user, replacing the one it had.</summary>
	/// <param name="target">The other user.</param>
	/// <param name="type">The kind of relationship.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	internal Task SetAsync(UserRecord target, RelationshipType type, CancellationToken cancellationToken)
	{
		return _user.Context.Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = _user.Record,
			Target = target,
			Type = type,
			CreatedAt = _user.Context.Clock.GetUtcNow()
		}, cancellationToken);
	}

	/// <summary>Removes a relationship of one kind toward another user, if it exists.</summary>
	/// <param name="target">The other user.</param>
	/// <param name="type">The kind of relationship.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	internal async Task RemoveAsync(UserRecord target, RelationshipType type, CancellationToken cancellationToken)
	{
		var existing = (await ListAsync(type, cancellationToken)).FirstOrDefault(relationship =>
			relationship.Target.Equals(target));
		if (existing is not null)
			await _user.Context.Relationships.DeleteAsync(existing, cancellationToken);
	}

	/// <summary>Adds another user as a friend.</summary>
	/// <param name="target">The other user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of adding the friend.</returns>
	internal async Task<UserOutcome> AddFriendAsync(UserRecord target, CancellationToken cancellationToken)
	{
		if (_user.Record.Equals(target)) return UserOutcome.Self;
		if (target.Value.DeletedAt is not null) return UserOutcome.Deleted;
		if (target.IsFriendOfEveryone()) return UserOutcome.Ok;

		await SetAsync(target, RelationshipType.Friend, cancellationToken);
		return UserOutcome.Ok;
	}

	/// <summary>Removes another user as a friend.</summary>
	/// <param name="target">The other user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of removing the friend.</returns>
	internal async Task<UserOutcome> RemoveFriendAsync(UserRecord target, CancellationToken cancellationToken)
	{
		if (_user.Record.Equals(target)) return UserOutcome.Self;
		if (target.IsFriendOfEveryone()) return UserOutcome.SystemAccount;

		await RemoveAsync(target, RelationshipType.Friend, cancellationToken);
		return UserOutcome.Ok;
	}

	/// <summary>Blocks another user.</summary>
	/// <param name="target">The other user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of blocking the user.</returns>
	internal async Task<UserOutcome> BlockAsync(UserRecord target, CancellationToken cancellationToken)
	{
		if (_user.Record.Equals(target)) return UserOutcome.Self;
		if (target.Value.DeletedAt is not null) return UserOutcome.Deleted;
		if (target.IsFriendOfEveryone()) return UserOutcome.SystemAccount;

		await SetAsync(target, RelationshipType.Block, cancellationToken);
		return UserOutcome.Ok;
	}

	/// <summary>Removes a block on another user.</summary>
	/// <param name="target">The other user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of removing the block.</returns>
	internal async Task<UserOutcome> UnblockAsync(UserRecord target, CancellationToken cancellationToken)
	{
		if (_user.Record.Equals(target)) return UserOutcome.Self;
		if (target.IsFriendOfEveryone()) return UserOutcome.Ok;

		await RemoveAsync(target, RelationshipType.Block, cancellationToken);
		return UserOutcome.Ok;
	}

	/// <summary>Lists the users this account has set a relationship toward, of one kind.</summary>
	/// <param name="type">The kind of relationship.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The users the relationships name.</returns>
	internal async Task<IReadOnlyList<UserRecord>> TargetsAsync(RelationshipType type,
		CancellationToken cancellationToken)
	{
		var relationships = await ListAsync(type, cancellationToken);
		return relationships.Select(relationship => relationship.Target).ToList();
	}
}