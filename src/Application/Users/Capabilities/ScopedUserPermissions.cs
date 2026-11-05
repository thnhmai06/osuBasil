using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Changes the permissions granted to an account.</summary>
public readonly struct ScopedUserPermissions(ScopedUser scoped)
{
	/// <summary>Replaces the permissions granted to this account.</summary>
	/// <param name="permissions">The permissions to grant.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of replacing the permissions.</returns>
	public async Task<Result<UserOutcome>> SetAsync(UserPermissions permissions,
		CancellationToken cancellationToken = default)
	{
		if (scoped.Guard(UserPermissions.UserManagePermissions, true) is { } denied) return denied;
		if (scoped.MissingPermissions(scoped.User.Record.Value.Permissions ^ permissions) is { } cannotGrant)
			return Result<UserOutcome>.Denied(cannotGrant);

		return await scoped.User.Grants.SetAsync(permissions, cancellationToken);
	}
}