using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Connections;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Users.Users;

/// <summary>An account scoped to one connection and its authority snapshot.</summary>
/// <remarks>
///     The snapshot is for one request or command; keeping it longer lets its rights outlive changes to permissions
///     or restrictions.
/// </remarks>
public sealed class ScopedUser : Scoped<UserPermissions, Connection>
{
	/// <summary>Creates a scoped account for a connection.</summary>
	/// <param name="user">The account to scope.</param>
	/// <param name="by">The connection acting on the account.</param>
	internal ScopedUser(User user, Connection by) : base(by,
		by.User.EffectivePermissions(user.Context.Clock.GetUtcNow()))
	{
		User = user;
		Granted = by.User.Record.Value.Permissions;
		Profile = new ScopedUserProfile(this);
		Credentials = new ScopedUserCredentials(this);
		Sessions = new ScopedUserSessions(this);
		Permissions = new ScopedUserPermissions(this);
		Restrictions = new ScopedUserRestrictions(this);
		Relationships = new ScopedUserRelationships(this);
	}

	/// <summary>Gets the account this scope is over.</summary>
	public User User { get; }

	/// <summary>The profile operations for this account.</summary>
	public ScopedUserProfile Profile { get; }

	/// <summary>The credential operations for this account.</summary>
	public ScopedUserCredentials Credentials { get; }

	/// <summary>The session operations for this account.</summary>
	public ScopedUserSessions Sessions { get; }

	/// <summary>The permission operations for this account.</summary>
	public ScopedUserPermissions Permissions { get; }

	/// <summary>The permissions granted to the connection's user when this scope was created.</summary>
	internal UserPermissions Granted { get; }

	/// <summary>The restriction operations for this account.</summary>
	public ScopedUserRestrictions Restrictions { get; }

	/// <summary>The friends and blocks this account sets toward other users.</summary>
	public ScopedUserRelationships Relationships { get; }

	/// <summary>Whether the caller is acting on their own account.</summary>
	internal bool IsSelf => By.Record.Equals(User.Record);

	/// <summary>Whether the caller's granted permissions strictly outrank the target's.</summary>
	internal bool Outranks => Granted.Allows(User.Record.Value.Permissions) && Granted != User.Record.Value.Permissions;

	/// <summary>Checks the required permission and account hierarchy.</summary>
	/// <param name="needed">The permission required.</param>
	/// <param name="selfAllowed">Whether the caller may act on their own account.</param>
	/// <returns><see langword="null" /> when allowed; otherwise, why the operation is unavailable.</returns>
	internal Result<UserOutcome>? Guard(UserPermissions needed, bool selfAllowed)
	{
		if (User.Record.IsBasilBot()) return UserOutcome.SystemAccount;
		if (Require(needed) is { } missing) return Result<UserOutcome>.Denied(missing);
		return (IsSelf && selfAllowed) || Outranks ? null : UserOutcome.NotOutranking;
	}

	/// <summary>Checks whether an area is locked for the caller.</summary>
	/// <param name="area">The area to check.</param>
	/// <param name="manager">The permission that manages the area and bypasses its lock.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the area is locked and the caller does not hold <paramref name="manager" />.</returns>
	internal async Task<bool> IsLockedAsync(Locks area, UserPermissions manager,
		CancellationToken cancellationToken)
	{
		return (await User.Context.Settings.GetAsync(cancellationToken)).Locks.HasFlag(area) &&
		       Require(manager) is not null;
	}

	internal UserPermissions? MissingPermissions(UserPermissions needed)
	{
		return Require(needed);
	}

	/// <summary>Permanently deletes this account.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of deleting the account.</returns>
	public async Task<Result<UserOutcome>> DeleteAsync(CancellationToken cancellationToken = default)
	{
		if (Guard(UserPermissions.UserManage, true) is { } denied) return denied;
		if (User.Record.Value.DeletedAt is not null) return UserOutcome.Ok;

		User.Grants.MarkDeleted(User.Context.Clock.GetUtcNow());
		await User.Context.Users.UpdateAsync(User.Record, cancellationToken);
		User.Context.Registry.CloseAll(User.Record, SessionEndReason.Deleted);
		return UserOutcome.Ok;
	}

}
