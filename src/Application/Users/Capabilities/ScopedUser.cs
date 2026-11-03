using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>An account scoped to one connection and its authority snapshot.</summary>
/// <remarks>
///     The snapshot is for one request or command; keeping it longer lets its rights outlive changes to permissions
///     or restrictions.
/// </remarks>
public readonly struct ScopedUser
{
	/// <summary>Gets the actor this scope is for.</summary>
	public Connection Actor { get; }

	/// <summary>Gets the permissions the actor held when the scope was created.</summary>
	public UserPermissions Rights { get; }

	/// <summary>Checks whether the actor holds the right an action needs.</summary>
	/// <param name="required">The right the action needs.</param>
	/// <returns><see langword="null" /> when it is held; otherwise what is missing.</returns>
	internal UserPermissions? Require(UserPermissions required)
	{
		return ScopedRights.Require(Rights, required);
	}

	/// <summary>Creates a scoped account for a connection.</summary>
	/// <param name="user">The account to scope.</param>
	/// <param name="actor">The connection acting on the account.</param>
	internal ScopedUser(User user, Connection actor)
	{
		Actor = actor;
		Rights = actor.EffectivePermissions(user.Context.Clock.GetUtcNow());
		User = user;
		Granted = actor.User.Record.Value.Permissions;
	}

	/// <summary>Gets the account this scope is over.</summary>
	public User User { get; }

	/// <summary>The profile operations for this account.</summary>
	public ScopedUserProfile Profile => new(this);

	/// <summary>The credential operations for this account.</summary>
	public ScopedUserCredentials Credentials => new(this);

	/// <summary>The session operations for this account.</summary>
	public ScopedUserSessions Sessions => new(this);

	/// <summary>The permission operations for this account.</summary>
	public ScopedUserPermissions Permissions => new(this);

	/// <summary>The permissions granted to the connection's user when this scope was created.</summary>
	internal UserPermissions Granted { get; }

	/// <summary>The restriction operations for this account.</summary>
	public ScopedUserRestrictions Restrictions => new(this);

	/// <summary>The friends and blocks this account sets toward other users.</summary>
	public ScopedUserRelationships Relationships => new(this);

	/// <summary>Whether the caller is acting on their own account.</summary>
	internal bool IsSelf => Actor.Record.Equals(User.Record);

	/// <summary>Whether the caller's granted permissions strictly outrank the target's.</summary>
	internal bool Outranks => Actor.Outranks(Granted, User.Record.Value.Permissions);

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

	/// <summary>Deletes this account, keeping it out of sign-in and every listing while its record remains.</summary>
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