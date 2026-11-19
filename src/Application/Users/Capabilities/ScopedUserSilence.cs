using Basil.Domain.Users;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUserSilence(ScopedUser scoped)
{
	public async Task<UserOutcome> ImposeAsync(DateTimeOffset endsAt, string? reason = null)
	{
		if (scoped.Deny(UserPrivileges.Moderator) is { } denied) return denied;
		if (await scoped.ResolveAsync() is not { } user) return UserOutcome.NotFound;
		if (scoped.Guard(user, false) is { } refused) return refused;
		return await user.Silence.ImposeAsync(endsAt, reason, scoped.Actor.Record);
	}

	public async Task<UserOutcome> LiftAsync(string? reason = null)
	{
		if (scoped.Deny(UserPrivileges.Moderator) is { } denied) return denied;
		if (await scoped.ResolveAsync() is not { } user) return UserOutcome.NotFound;
		if (scoped.Guard(user, false) is { } refused) return refused;
		return await user.Silence.LiftAsync(reason, scoped.Actor.Record);
	}
}