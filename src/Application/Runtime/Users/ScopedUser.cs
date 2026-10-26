using Basil.Application.Runtime.Sessions;
using Basil.Application.Persistence.Common;
using Basil.Application.Capabilities.Users;
using Basil.Application.Runtime.Common;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Users;

/// <summary>An account scoped to one connection and its authority snapshot.</summary>
/// <remarks>The snapshot is for one request or command; keeping it longer lets its rights outlive changes to permissions or restrictions.</remarks>
public sealed class ScopedUser : Scoped<UserPermissions>
{
	/// <summary>Gets the account this scope is over.</summary>
	public User User { get; }

	/// <summary>The permissions granted to the connection's user when this scope was created.</summary>
	internal UserPermissions Granted { get; }

	/// <summary>The restriction operations for this account.</summary>
	public ScopedUserRestrictions Restrictions { get; }

	/// <summary>The friends and blocks this account sets toward other users.</summary>
	public ScopedUserRelationships Relationships { get; }

	/// <summary>Creates a scoped account for a connection.</summary>
	/// <param name="user">The account to scope.</param>
	/// <param name="by">The connection acting on the account.</param>
	internal ScopedUser(User user, Connection by) : base(by, by.User.EffectivePermissions(user.Context.Clock.GetUtcNow()))
	{
		User = user;
		Granted = by.User.Record.Value.Permissions;
		Restrictions = new ScopedUserRestrictions(this);
		Relationships = new ScopedUserRelationships(this);
	}

	/// <summary>Sets or clears the away message other users see.</summary>
	/// <param name="message">The away message, or <see langword="null" /> or white space to clear it.</param>
	/// <returns><see langword="true" /> if this connection belongs to this user and the message was set.</returns>
	public bool SetAway(string? message)
	{
		if (!ReferenceEquals(By.User, User)) return false;

		User.AwayMessage = string.IsNullOrWhiteSpace(message) ? null : message;
		return true;
	}

	/// <summary>Sets whether this user accepts private messages only from their friends.</summary>
	/// <param name="pmPrivate"><see langword="true" /> to accept private messages only from friends.</param>
	/// <returns><see langword="true" /> if this connection belongs to this user and the setting was set.</returns>
	public bool SetPmPrivate(bool pmPrivate)
	{
		if (!ReferenceEquals(By.User, User)) return false;

		User.PmPrivate = pmPrivate;
		return true;
	}

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
		if (User.Record.IsBasilBot()) return UserOutcome.Protected;
		if (Require(needed) is { } missing) return Result<UserOutcome>.Denied(missing);
		return (IsSelf && selfAllowed) || Outranks ? null : UserOutcome.NotOutranking;
	}

	/// <summary>Replaces this account's permissions.</summary>
	/// <param name="permissions">The permissions to grant.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of replacing the permissions.</returns>
	public async Task<Result<UserOutcome>> SetPermissionsAsync(UserPermissions permissions,
		CancellationToken cancellationToken = default)
	{
		if (Guard(UserPermissions.UserManagePermissions, true) is { } denied) return denied;
		if (Require(User.Record.Value.Permissions ^ permissions) is { } cannotGrant)
			return Result<UserOutcome>.Denied(cannotGrant);

		User.ReplacePermissions(permissions);
		await User.Context.Users.UpdateAsync(User.Record, cancellationToken);
		if (User.Context.Registry.Online.GetValueOrDefault(User.Record) is { } user)
			User.Context.Registry.CloseDisallowed(user);

		User.Context.Events.Publish(new UserPermissionsChanged(User.Record, permissions));
		return UserOutcome.Ok;
	}

	/// <summary>Permanently deletes this account.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of deleting the account.</returns>
	public async Task<Result<UserOutcome>> DeleteAsync(CancellationToken cancellationToken = default)
	{
		if (Guard(UserPermissions.UserManage, true) is { } denied) return denied;
		if (User.Record.Value.DeletedAt is not null) return UserOutcome.Ok;

		User.MarkDeleted(User.Context.Clock.GetUtcNow());
		await User.Context.Users.UpdateAsync(User.Record, cancellationToken);
		User.Context.Registry.CloseAll(User.Record, ConnectionCloseReason.Deleted);
		return UserOutcome.Ok;
	}

	/// <summary>Reads the login history of this account.</summary>
	/// <param name="page">Which part of the history to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when it is Ok, one part of the history, newest first.</returns>
	/// <remarks>Reading the history of another account is recorded in the moderation log.</remarks>
	public async Task<(Result<UserOutcome> Result, Page<LoginEntry>? Logins)> ViewLoginsAsync(PageRequest page,
		CancellationToken cancellationToken = default)
	{
		if (Guard(UserPermissions.UserViewLogins, true) is { } denied) return (denied, null);

		if (!IsSelf)
			await User.Context.ModerationLog.AppendAsync(new ModerationData
			{
				Action = ModerationAction.ViewLogins,
				By = By.Record,
				Target = User.Record,
				Timestamp = User.Context.Clock.GetUtcNow()
			}, cancellationToken);

		return (UserOutcome.Ok, await User.Context.Logins.ListAsync(User.Record, page, cancellationToken));
	}

	/// <summary>Closes every connection of this account.</summary>
	/// <returns>Ok, or why the caller may not.</returns>
	/// <remarks>
	///     An account may close its own connections; closing another's needs <see cref="UserPermissions.UserManage" /> and
	///     outranking them. The connections, the caller's own included, close as
	///     <see cref="ConnectionCloseReason.Revoked" />.
	/// </remarks>
	public Result<UserOutcome> RevokeConnections()
	{
		if (User.Record.IsBasilBot()) return UserOutcome.Protected;
		if (!IsSelf && Guard(UserPermissions.UserManage, false) is { } denied) return denied;

		User.Context.Registry.CloseAll(User.Record, ConnectionCloseReason.Revoked);
		return UserOutcome.Ok;
	}

	/// <summary>Replaces this account's password and closes every connection of it.</summary>
	/// <param name="newPasswordHash">The MD5 digest of the new password.</param>
	/// <param name="currentPasswordHash">The MD5 digest of the current password, needed when users change their own.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok, WrongPassword, or why the caller may not.</returns>
	/// <remarks>
	///     An account changes its own password by giving the current one; changing another's needs
	///     <see cref="UserPermissions.UserManage" /> and outranking them. Every connection, the caller's own included, closes as
	///     <see cref="ConnectionCloseReason.CredentialsChanged" />.
	/// </remarks>
	public async Task<Result<UserOutcome>> ChangePasswordAsync(Md5 newPasswordHash, Md5? currentPasswordHash,
		CancellationToken cancellationToken = default)
	{
		if (User.Record.IsBasilBot()) return UserOutcome.Protected;

		if (IsSelf)
		{
			if (currentPasswordHash is not { } current || !await User.Context.Credentials.IsPasswordAsync(User.Record, current, cancellationToken))
				return UserOutcome.WrongPassword;
		}
		else if (Guard(UserPermissions.UserManage, false) is { } denied)
		{
			return denied;
		}

		await User.Context.Credentials.CreateOrUpdateAsync(new Credentials(User.Record, newPasswordHash), cancellationToken);
		User.Context.Registry.CloseAll(User.Record, ConnectionCloseReason.CredentialsChanged);
		return UserOutcome.Ok;
	}
}