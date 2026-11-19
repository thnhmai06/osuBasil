using Basil.Domain.Users;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUserSilence(ScopedUser scoped)
{
	public async Task<UserActionResult> ImposeAsync(DateTimeOffset endsAt, string? reason = null)
	{
		if (scoped.Deny(UserPrivileges.Moderator) is { } denied) return denied;
		if (await scoped.ResolveAsync() is not { } user) return UserActionResult.NotFound;
		if (scoped.Guard(user, false) is { } refused) return refused;
		return await user.Silence.ImposeAsync(endsAt, reason, scoped.Actor.Record);
	}

	public async Task<UserActionResult> LiftAsync(string? reason = null)
	{
		if (scoped.Deny(UserPrivileges.Moderator) is { } denied) return denied;
		if (await scoped.ResolveAsync() is not { } user) return UserActionResult.NotFound;
		if (scoped.Guard(user, false) is { } refused) return refused;
		return await user.Silence.LiftAsync(reason, scoped.Actor.Record);
	}
}