using Basil.Application.Common.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Changes the permissions granted to an account.</summary>
public sealed class ScopedUserPermissions(ScopedUser scoped)
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

		scoped.User.Grants.Replace(permissions);
		await scoped.User.Context.Users.UpdateAsync(scoped.User.Record, cancellationToken);
		if (scoped.User.Context.Registry.Online.GetValueOrDefault(scoped.User.Record) is { } user)
			scoped.User.Context.Registry.CloseDisallowed(user);

		scoped.User.Context.Events.Publish(new UserPermissionsChanged(scoped.User.Record, permissions));
		return UserOutcome.Ok;
	}
}