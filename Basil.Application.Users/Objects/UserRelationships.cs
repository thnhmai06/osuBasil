using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

public sealed class UserRelationships
{
	private readonly User _user;

	internal UserRelationships(User user)
	{
		_user = user;
	}

	internal async Task<IReadOnlyList<Relationship>> ListAsync(RelationshipType type,
		CancellationToken ct = default)
	{
		var all = await _user.Context.Relationships.ListAsync(_user.Record, ct);
		return all.Where(relationship => relationship.Type == type).ToList();
	}

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

	internal async Task<bool> RemoveAsync(UserRecord target, RelationshipType type)
	{
		var existing = (await ListAsync(type)).FirstOrDefault(relationship =>
			relationship.Target.Equals(target));
		if (existing is null) return false;

		await _user.Context.Relationships.DeleteAsync(existing);
		return true;
	}

	internal async Task<UserActionResult> AddFriendAsync(UserRecord target)
	{
		if (_user.Record.Equals(target)) return UserActionResult.Self;
		if (target.Value.DeletedAt is not null) return UserActionResult.Deleted;
		if (target.Id == BasilBot.Id) return UserActionResult.Ok;

		await SetAsync(target, RelationshipType.Friend);
		return UserActionResult.Ok;
	}

	internal async Task<UserActionResult> RemoveFriendAsync(UserRecord target)
	{
		if (_user.Record.Equals(target)) return UserActionResult.Self;
		if (target.Id == BasilBot.Id) return UserActionResult.SystemAccount;

		return await RemoveAsync(target, RelationshipType.Friend) ? UserActionResult.Ok : UserActionResult.NotFound;
	}

	internal async Task<UserActionResult> BlockAsync(UserRecord target)
	{
		if (_user.Record.Equals(target)) return UserActionResult.Self;
		if (target.Value.DeletedAt is not null) return UserActionResult.Deleted;
		if (target.Id == BasilBot.Id) return UserActionResult.SystemAccount;

		await SetAsync(target, RelationshipType.Block);
		return UserActionResult.Ok;
	}

	internal async Task<UserActionResult> UnblockAsync(UserRecord target)
	{
		if (_user.Record.Equals(target)) return UserActionResult.Self;
		if (target.Id == BasilBot.Id) return UserActionResult.NotFound;

		return await RemoveAsync(target, RelationshipType.Block) ? UserActionResult.Ok : UserActionResult.NotFound;
	}

	internal async Task<IReadOnlyList<UserRecord>> TargetsAsync(RelationshipType type,
		CancellationToken ct)
	{
		var relationships = await ListAsync(type, ct);
		return relationships.Select(relationship => relationship.Target).ToList();
	}
}