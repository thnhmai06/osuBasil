using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

public sealed class UserRelationships
{
	private readonly User _user;

	internal UserRelationships(User user)
	{
		_user = user;
	}

	internal Task SetAsync(UserRecord target, RelationshipType type)
	{
		return _user.Context.Relationships.SetAsync(_user.Record, target, type);
	}

	internal Task<bool> RemoveAsync(UserRecord target, RelationshipType type)
	{
		return _user.Context.Relationships.RemoveAsync(_user.Record, target, type);
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
		var ids = await _user.Context.Relationships.OfAsync(_user.Record, type, ct);
		return await _user.Context.Users.GetManyAsync([.. ids], ct);
	}
}
