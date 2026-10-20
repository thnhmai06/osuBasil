using Basil.Domain.Users;

namespace Basil.Application.Objects.Users;

/// <summary>Sets and removes the friends and blocks held toward other users.</summary>
public sealed class ScopedUserRelationships
{
	private readonly ScopedUser _scoped;

	/// <summary>Creates relationship operations scoped to an account.</summary>
	/// <param name="scoped">The account scope.</param>
	internal ScopedUserRelationships(ScopedUser scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Adds the target as a friend.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of adding the friend.</returns>
	public async Task<UserResult> AddFriendAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.IsSelf) return UserResult.Self;

		var target = _scoped.User.Record;
		if (target.Value.DeletedAt is not null) return UserResult.Deleted;
		if (BasilBot.Is(target)) return UserResult.Ok;

		await _scoped.User.Context.Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = _scoped.By.Record,
			Target = target,
			Type = RelationshipType.Friend
		}, cancellationToken);
		return UserResult.Ok;
	}

	/// <summary>Removes the target as a friend.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of removing the friend.</returns>
	public async Task<UserResult> RemoveFriendAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.IsSelf) return UserResult.Self;

		var target = _scoped.User.Record;
		if (BasilBot.Is(target)) return UserResult.Protected;

		var relationships = await _scoped.User.Context.Relationships.ListAsync(_scoped.By.Record, cancellationToken);
		var friend = relationships.FirstOrDefault(relationship => relationship.Target.Equals(target) &&
		                                                          relationship.Type == RelationshipType.Friend);
		if (friend is not null)
			await _scoped.User.Context.Relationships.DeleteAsync(friend, cancellationToken);
		return UserResult.Ok;
	}

	/// <summary>Blocks the target.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of blocking the target.</returns>
	public async Task<UserResult> BlockAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.IsSelf) return UserResult.Self;

		var target = _scoped.User.Record;
		if (target.Value.DeletedAt is not null) return UserResult.Deleted;
		if (BasilBot.Is(target)) return UserResult.Protected;

		await _scoped.User.Context.Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = _scoped.By.Record,
			Target = target,
			Type = RelationshipType.Block
		}, cancellationToken);
		return UserResult.Ok;
	}

	/// <summary>Removes a block on the target.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of removing the block.</returns>
	public async Task<UserResult> UnblockAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.IsSelf) return UserResult.Self;

		var target = _scoped.User.Record;
		if (BasilBot.Is(target)) return UserResult.Ok;

		var relationships = await _scoped.User.Context.Relationships.ListAsync(_scoped.By.Record, cancellationToken);
		var block = relationships.FirstOrDefault(relationship => relationship.Target.Equals(target) &&
		                                                         relationship.Type == RelationshipType.Block);
		if (block is not null)
			await _scoped.User.Context.Relationships.DeleteAsync(block, cancellationToken);
		return UserResult.Ok;
	}
}