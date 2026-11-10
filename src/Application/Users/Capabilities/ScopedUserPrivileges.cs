using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Changes the privileges granted to an account.</summary>
public readonly struct ScopedUserPrivileges(ScopedUser scoped)
{
	/// <summary>Replaces the privileges granted to this account.</summary>
	/// <param name="privileges">The privileges to grant; the supporter privilege is added to a tournament staff member.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of replacing the privileges.</returns>
	/// <remarks>Only privileges the caller holds themselves can be granted or taken away.</remarks>
	public async Task<Result<UserOutcome>> SetAsync(UserPrivileges privileges,
		CancellationToken cancellationToken = default)
	{
		privileges = privileges.Completed();
		if (scoped.Guard(UserPrivileges.Owner, true) is { } denied) return denied;
		if (scoped.Require(scoped.User.Record.Value.Privileges ^ privileges) is { } cannotGrant)
			return Result<UserOutcome>.Denied(cannotGrant);

		return await scoped.User.Grants.SetAsync(privileges, cancellationToken);
	}
}
