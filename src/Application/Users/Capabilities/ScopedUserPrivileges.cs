using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Changes the privileges granted to an account.</summary>
public readonly struct ScopedUserPrivileges(ScopedUser scoped)
{
	/// <summary>Replaces the privileges granted to this account.</summary>
	/// <param name="privileges">The privileges to grant; the supporter privilege is added to a tournament staff member.</param>
	/// <returns>The outcome of replacing the privileges.</returns>
	/// <remarks>Only privileges the caller holds themselves can be granted or taken away.</remarks>
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
