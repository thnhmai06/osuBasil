using Basil.Common.Application;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Users;

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
	public async Task<Result<UserOutcome>> AddFriendAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.IsSelf) return UserOutcome.Self;

		var target = _scoped.User.Record;
		if (target.Value.DeletedAt is not null) return UserOutcome.Deleted;
		if (target.IsBasilBot()) return UserOutcome.Ok;

		await _scoped.User.Context.Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = _scoped.By.Record,
			Target = target,
			Type = RelationshipType.Friend,
			CreatedAt = _scoped.User.Context.Clock.GetUtcNow()
		}, cancellationToken);
		return UserOutcome.Ok;
	}

	/// <summary>Removes the target as a friend.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of removing the friend.</returns>
	public async Task<Result<UserOutcome>> RemoveFriendAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.IsSelf) return UserOutcome.Self;

		var target = _scoped.User.Record;
		if (target.IsBasilBot()) return UserOutcome.Protected;

		var relationships = await _scoped.User.Context.Relationships.ListAsync(_scoped.By.Record, cancellationToken);
		var friend = relationships.FirstOrDefault(relationship => relationship.Target.Equals(target) &&
		                                                          relationship.Type == RelationshipType.Friend);
		if (friend is not null)
			await _scoped.User.Context.Relationships.DeleteAsync(friend, cancellationToken);
		return UserOutcome.Ok;
	}

	/// <summary>Blocks the target.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of blocking the target.</returns>
	public async Task<Result<UserOutcome>> BlockAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.IsSelf) return UserOutcome.Self;

		var target = _scoped.User.Record;
		if (target.Value.DeletedAt is not null) return UserOutcome.Deleted;
		if (target.IsBasilBot()) return UserOutcome.Protected;

		await _scoped.User.Context.Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = _scoped.By.Record,
			Target = target,
			Type = RelationshipType.Block,
			CreatedAt = _scoped.User.Context.Clock.GetUtcNow()
		}, cancellationToken);
		return UserOutcome.Ok;
	}

	/// <summary>Removes a block on the target.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of removing the block.</returns>
	public async Task<Result<UserOutcome>> UnblockAsync(CancellationToken cancellationToken = default)
	{
		if (_scoped.IsSelf) return UserOutcome.Self;

		var target = _scoped.User.Record;
		if (target.IsBasilBot()) return UserOutcome.Ok;

		var relationships = await _scoped.User.Context.Relationships.ListAsync(_scoped.By.Record, cancellationToken);
		var block = relationships.FirstOrDefault(relationship => relationship.Target.Equals(target) &&
		                                                         relationship.Type == RelationshipType.Block);
		if (block is not null)
			await _scoped.User.Context.Relationships.DeleteAsync(block, cancellationToken);
		return UserOutcome.Ok;
	}
}