using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUserRelationships
{
	private readonly ScopedUser _scoped;

	internal ScopedUserRelationships(ScopedUser scoped)
	{
		_scoped = scoped;
	}

	public async Task<UserActionResult> AddFriendAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserActionResult.NotFound;
		return await _scoped.Actor.User.Relationships.AddFriendAsync(target.Record);
	}

	public async Task<UserActionResult> RemoveFriendAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserActionResult.NotFound;
		return await _scoped.Actor.User.Relationships.RemoveFriendAsync(target.Record);
	}

	public async Task<UserActionResult> BlockAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserActionResult.NotFound;
		return await _scoped.Actor.User.Relationships.BlockAsync(target.Record);
	}

	public async Task<UserActionResult> UnblockAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserActionResult.NotFound;
		return await _scoped.Actor.User.Relationships.UnblockAsync(target.Record);
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
		if (_scoped is { IsSelf: false, Actor: not OperatorSession }) return (UserActionResult.NotSelf, []);

		if (await _scoped.ResolveAsync(ct) is not { } user) return (UserActionResult.NotFound, []);

		return (UserActionResult.Ok, await user.Relationships.TargetsAsync(type, ct));
	}
}