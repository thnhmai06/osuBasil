using Basil.Application.Users.Objects;
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

	public async Task<UserOutcome> AddFriendAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserOutcome.NotFound;
		return await _scoped.Actor.User.Relationships.AddFriendAsync(target.Record);
	}

	public async Task<UserOutcome> RemoveFriendAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserOutcome.NotFound;
		return await _scoped.Actor.User.Relationships.RemoveFriendAsync(target.Record);
	}

	public async Task<UserOutcome> BlockAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserOutcome.NotFound;
		return await _scoped.Actor.User.Relationships.BlockAsync(target.Record);
	}

	public async Task<UserOutcome> UnblockAsync()
	{
		if (await _scoped.ResolveAsync() is not { } target) return UserOutcome.NotFound;
		return await _scoped.Actor.User.Relationships.UnblockAsync(target.Record);
	}

	public Task<(UserOutcome Result, IReadOnlyList<UserRecord> Users)> FriendsAsync(
		CancellationToken ct = default)
	{
		return ListAsync(RelationshipType.Friend, ct);
	}

	public Task<(UserOutcome Result, IReadOnlyList<UserRecord> Users)> BlocksAsync(
		CancellationToken ct = default)
	{
		return ListAsync(RelationshipType.Block, ct);
	}

	private async Task<(UserOutcome Result, IReadOnlyList<UserRecord> Users)> ListAsync(
		RelationshipType type, CancellationToken ct)
	{
		// The operator reads any account's lists; everyone else reads their own.
		if (_scoped is { IsSelf: false, Actor: not OperatorSession }) return (UserOutcome.NotSelf, []);

		if (await _scoped.ResolveAsync(ct) is not { } user) return (UserOutcome.NotFound, []);

		return (UserOutcome.Ok, await user.Relationships.TargetsAsync(type, ct));
	}
}