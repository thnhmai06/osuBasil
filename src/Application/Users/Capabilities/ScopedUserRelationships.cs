using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Sets and removes the friends and blocks held toward other users.</summary>
public readonly struct ScopedUserRelationships
{
	private readonly ScopedUser _scoped;

	/// <summary>Creates relationship operations scoped to an account.</summary>
	/// <param name="scoped">The account scope.</param>
	internal ScopedUserRelationships(ScopedUser scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Adds the target as a friend.</summary>
	/// <returns>The outcome of adding the friend.</returns>
	public async Task<Result<UserOutcome>> AddFriendAsync()
	{
		return await _scoped.Actor.User.Relationships.AddFriendAsync(_scoped.User.Record);
	}

	/// <summary>Removes the target as a friend.</summary>
	/// <returns>The outcome of removing the friend.</returns>
	public async Task<Result<UserOutcome>> RemoveFriendAsync()
	{
		return await _scoped.Actor.User.Relationships.RemoveFriendAsync(_scoped.User.Record);
	}

	/// <summary>Blocks the target.</summary>
	/// <returns>The outcome of blocking the target.</returns>
	public async Task<Result<UserOutcome>> BlockAsync()
	{
		return await _scoped.Actor.User.Relationships.BlockAsync(_scoped.User.Record);
	}

	/// <summary>Removes a block on the target.</summary>
	/// <returns>The outcome of removing the block.</returns>
	public async Task<Result<UserOutcome>> UnblockAsync()
	{
		return await _scoped.Actor.User.Relationships.UnblockAsync(_scoped.User.Record);
	}

	/// <summary>Lists the users this account has friended.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the friends; only the account's own user may read them.</returns>
	public Task<(Result<UserOutcome> Result, IReadOnlyList<UserRecord> Users)> FriendsAsync(
		CancellationToken cancellationToken = default)
	{
		return ListAsync(RelationshipType.Friend, cancellationToken);
	}

	/// <summary>Lists the users this account has blocked.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the blocked users; only the account's own user may read them.</returns>
	public Task<(Result<UserOutcome> Result, IReadOnlyList<UserRecord> Users)> BlocksAsync(
		CancellationToken cancellationToken = default)
	{
		return ListAsync(RelationshipType.Block, cancellationToken);
	}

	private async Task<(Result<UserOutcome> Result, IReadOnlyList<UserRecord> Users)> ListAsync(
		RelationshipType type, CancellationToken cancellationToken)
	{
		// The operator reads any account's lists; everyone else reads their own.
		if (_scoped is { IsSelf: false, Actor: not OperatorConnection }) return (UserOutcome.NotSelf, []);

		return (UserOutcome.Ok, await _scoped.User.Relationships.TargetsAsync(type, cancellationToken));
	}
}