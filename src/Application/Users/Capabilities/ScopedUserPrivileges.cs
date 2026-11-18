using Basil.Application.Users.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUserPrivileges(ScopedUser scoped)
{
	public async Task<UserOutcome> SetAsync(UserPrivileges privileges)
	{
		privileges = privileges.Completed();
		if (scoped.Deny(UserPrivileges.Owner) is { } denied) return denied;
		if (await scoped.ResolveAsync() is not { } user) return UserOutcome.NotFound;
		if (scoped.Guard(user, true) is { } refused) return refused;
		if (scoped.Deny(user.Record.Value.Privileges ^ privileges) is { } cannotGrant) return cannotGrant;

		return await user.Grants.SetAsync(privileges);
	}
}