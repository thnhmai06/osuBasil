using Basil.Domain.Users;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUserPrivileges(ScopedUser scoped)
{
	public async Task<UserActionResult> SetAsync(UserPrivileges privileges)
	{
		privileges = privileges.Completed();
		if (scoped.Deny(UserPrivileges.Owner) is { } denied) return denied;
		if (await scoped.ResolveAsync() is not { } user) return UserActionResult.NotFound;
		if (scoped.Guard(user, true) is { } refused) return refused;
		if (scoped.Deny(user.Record.Value.Privileges ^ privileges) is { } cannotGrant) return cannotGrant;

		return await user.SetPrivilegesAsync(privileges);
	}
}