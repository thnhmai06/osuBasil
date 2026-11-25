using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUserRelationships
{
	private readonly ScopedUser _scoped;

	internal ScopedUserRelationships(ScopedUser scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Adds the other account to the friends of this account, which only the account itself may do.</summary>
	public async Task<UserActionResult> AddFriendAsync(int otherId)
	{
		if (!_scoped.IsSelf) return UserActionResult.NotSelf;
		if (await _scoped.Other(otherId).ResolveAsync() is not { } target) return UserActionResult.NotFound;
		return await _scoped.ActorUser.Relationships.AddFriendAsync(target.Record);
	}

	/// <summary>Removes the other account from the friends of this account, which only the account itself may do.</summary>
	public async Task<UserActionResult> RemoveFriendAsync(int otherId)
	{
		if (!_scoped.IsSelf) return UserActionResult.NotSelf;
		if (await _scoped.Other(otherId).ResolveAsync() is not { } target) return UserActionResult.NotFound;
		return await _scoped.ActorUser.Relationships.RemoveFriendAsync(target.Record);
	}

	/// <summary>Adds the other account to the blocks of this account, which only the account itself may do.</summary>
	public async Task<UserActionResult> BlockAsync(int otherId)
	{
		if (!_scoped.IsSelf) return UserActionResult.NotSelf;
		if (await _scoped.Other(otherId).ResolveAsync() is not { } target) return UserActionResult.NotFound;
		return await _scoped.ActorUser.Relationships.BlockAsync(target.Record);
	}

	/// <summary>Removes the other account from the blocks of this account, which only the account itself may do.</summary>
	public async Task<UserActionResult> UnblockAsync(int otherId)
	{
		if (!_scoped.IsSelf) return UserActionResult.NotSelf;
		if (await _scoped.Other(otherId).ResolveAsync() is not { } target) return UserActionResult.NotFound;
		return await _scoped.ActorUser.Relationships.UnblockAsync(target.Record);
	}

	public Task<(UserActionResult Result, IReadOnlyList<UserRecord> Users)> FriendsAsync(
		CancellationToken ct = default)
	{
		return ListAsync(RelationshipType.Friend, ct);
	}

	public Task<(UserActionResult Result, IReadOnlyList<UserRecord> Users)> BlocksAsync(
		CancellationToken ct = default)
	{
		return ListAsync(RelationshipType.Block, ct);
	}

	private async Task<(UserActionResult Result, IReadOnlyList<UserRecord> Users)> ListAsync(
		RelationshipType type, CancellationToken ct)
	{
		// The operator reads any account's lists; everyone else reads their own.
		if (_scoped is { IsSelf: false, IsOperator: false }) return (UserActionResult.NotSelf, []);

		if (await _scoped.ResolveAsync(ct) is not { } user) return (UserActionResult.NotFound, []);

		return (UserActionResult.Ok, await user.Relationships.TargetsAsync(type, ct));
	}
}