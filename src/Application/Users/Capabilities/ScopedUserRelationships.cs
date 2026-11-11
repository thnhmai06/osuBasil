using Basil.Application.Shared;
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
	public async Task<UserOutcome> AddFriendAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserOutcome.NotFound;
		return await _scoped.Actor.User.Relationships.AddFriendAsync(target.Record);
	}

	/// <summary>Removes the target as a friend.</summary>
	/// <returns>The outcome of removing the friend.</returns>
	public async Task<UserOutcome> RemoveFriendAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserOutcome.NotFound;
		return await _scoped.Actor.User.Relationships.RemoveFriendAsync(target.Record);
	}

	/// <summary>Blocks the target.</summary>
	/// <returns>The outcome of blocking the target.</returns>
	public async Task<UserOutcome> BlockAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserOutcome.NotFound;
		return await _scoped.Actor.User.Relationships.BlockAsync(target.Record);
	}

	/// <summary>Removes a block on the target.</summary>
	/// <returns>The outcome of removing the block.</returns>
	public async Task<UserOutcome> UnblockAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserOutcome.NotFound;
		return await _scoped.Actor.User.Relationships.UnblockAsync(target.Record);
	}

	/// <summary>Lists the users this account has friended.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the friends; only the account's own user may read them.</returns>
	public Task<(UserOutcome Result, IReadOnlyList<UserRecord> Users)> FriendsAsync(
		CancellationToken ct = default)
	{
		return ListAsync(RelationshipType.Friend, ct);
	}

	/// <summary>Lists the users this account has blocked.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the blocked users; only the account's own user may read them.</returns>
	public Task<(UserOutcome Result, IReadOnlyList<UserRecord> Users)> BlocksAsync(
		CancellationToken ct = default)
	{
		return ListAsync(RelationshipType.Block, ct);
	}

	private async Task<(UserOutcome Result, IReadOnlyList<UserRecord> Users)> ListAsync(
		RelationshipType type, CancellationToken ct)
	{
		// The operator reads any account's lists; everyone else reads their own.
		if (_scoped is { IsSelf: false, Actor: not OperatorConnection }) return (UserOutcome.NotSelf, []);

		if (await _scoped.ResolveAsync(ct) is not { } user) return (UserOutcome.NotFound, []);

		return (UserOutcome.Ok, await user.Relationships.TargetsAsync(type, ct));
	}
}