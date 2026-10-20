using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Users;

/// <summary>An account scoped to one connection and its authority snapshot.</summary>
/// <remarks>The snapshot is for one request or command; keeping it longer lets its rights outlive changes to permissions or restrictions.</remarks>
public sealed class ScopedUser
{
	private readonly DateTimeOffset _now;

	/// <summary>The account this scope is over.</summary>
	public User User { get; }

	/// <summary>The connection acting on the account.</summary>
	public Connection By { get; }

	/// <summary>The permissions granted to the connection's user when this scope was created.</summary>
	internal Permissions Granted { get; }

	/// <summary>The restriction operations for this account.</summary>
	public ScopedUserRestrictions Restrictions { get; }

	/// <summary>The friends and blocks this account sets toward other users.</summary>
	public ScopedUserRelationships Relationships { get; }

	/// <summary>Creates a scoped account for a connection.</summary>
	/// <param name="user">The account to scope.</param>
	/// <param name="by">The connection acting on the account.</param>
	internal ScopedUser(User user, Connection by)
	{
		User = user;
		By = by;
		_now = user.Context.Clock.GetUtcNow();
		Granted = by.Session.User.Value.Permissions;
		Restrictions = new ScopedUserRestrictions(this);
		Relationships = new ScopedUserRelationships(this);
	}

	/// <summary>Checks whether the caller has a required permission in effect.</summary>
	/// <param name="needed">The permission required.</param>
	/// <returns><see langword="null" /> when allowed; otherwise, why the permission is unavailable.</returns>
	internal UserResult? Require(Permissions needed)
	{
		return By.Session.Check(needed, _now) switch
		{
			Access.NotGranted => UserResult.NotAuthorized,
			Access.Suspended => UserResult.Restricted,
			_ => null
		};
	}

	/// <summary>Whether the caller is acting on their own account.</summary>
	internal bool IsSelf => By.User.Equals(User.Record);

	/// <summary>Whether the caller's granted permissions strictly outrank the target's.</summary>
	internal bool Outranks => Granted.Allows(User.Record.Value.Permissions) && Granted != User.Record.Value.Permissions;

	/// <summary>Checks the required permission and account hierarchy.</summary>
	/// <param name="needed">The permission required.</param>
	/// <param name="selfAllowed">Whether the caller may act on their own account.</param>
	/// <returns><see langword="null" /> when allowed; otherwise, why the operation is unavailable.</returns>
	internal UserResult? Guard(Permissions needed, bool selfAllowed)
	{
		if (BasilBot.Is(User.Record)) return UserResult.Protected;
		if (Require(needed) is { } denied) return denied;
		return (IsSelf && selfAllowed) || Outranks ? null : UserResult.NotAuthorized;
	}

	/// <summary>Replaces this account's permissions.</summary>
	/// <param name="permissions">The permissions to grant.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome of replacing the permissions.</returns>
	public async Task<UserResult> SetPermissionsAsync(Permissions permissions,
		CancellationToken ct = default)
	{
		if (Guard(Permissions.UserManagePermissions, selfAllowed: true) is { } denied) return denied;
		if (!Granted.Allows(User.Record.Value.Permissions ^ permissions)) return UserResult.NotAuthorized;

		User.Apply(user => user.Permissions = permissions);
		await User.Context.Users.UpdateAsync(User.Record, ct);
		if (User.Context.Registry.Sessions.GetValueOrDefault(User.Record) is { } session)
			User.Context.Sessions.CloseDisallowed(session);

		User.Context.Events.Emit(new UserPermissionsChanged(User.Record, permissions));
		return UserResult.Ok;
	}

	/// <summary>Permanently deletes this account.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome of deleting the account.</returns>
	public async Task<UserResult> DeleteAsync(CancellationToken ct = default)
	{
		if (Guard(Permissions.UserManage, selfAllowed: true) is { } denied) return denied;
		if (User.Record.Value.DeletedAt is not null) return UserResult.Ok;

		var now = User.Context.Clock.GetUtcNow();
		User.Apply(user =>
		{
			user.DeletedAt = now;
			user.Permissions = Permissions.None;
		});

		await User.Context.Users.UpdateAsync(User.Record, ct);
		User.Context.Sessions.CloseAll(User.Record, ConnectionCloseReason.Deleted);
		return UserResult.Ok;
	}

	/// <summary>Reads the login history of this account.</summary>
	/// <param name="page">Which part of the history to return.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome and, when it is Ok, one part of the history, newest first.</returns>
	/// <remarks>Reading the history of another account is recorded in the moderation log.</remarks>
	public async Task<(UserResult Result, Page<LoginEntry>? Logins)> ViewLoginsAsync(PageRequest page,
		CancellationToken ct = default)
	{
		if (Guard(Permissions.UserViewLogins, selfAllowed: true) is { } denied) return (denied, null);

		if (!IsSelf)
		{
			await User.Context.ModerationLog.AppendAsync(new ModerationLogData
			{
				Action = ModerationAction.ViewLogins,
				By = By.User,
				Target = User.Record,
				Timestamp = _now
			}, ct);
		}

		return (UserResult.Ok, await User.Context.Logins.ListAsync(User.Record, page, ct));
	}

	/// <summary>Closes every connection of this account.</summary>
	/// <returns>Ok, or why the caller may not.</returns>
	/// <remarks>
	///     An account may close its own connections; closing another's needs <see cref="Permissions.UserManage" /> and
	///     outranking them. The connections, the caller's own included, close as
	///     <see cref="ConnectionCloseReason.Revoked" />.
	/// </remarks>
	public UserResult RevokeConnections()
	{
		if (BasilBot.Is(User.Record)) return UserResult.Protected;
		if (!IsSelf && Guard(Permissions.UserManage, selfAllowed: false) is { } denied) return denied;

		User.Context.Sessions.CloseAll(User.Record, ConnectionCloseReason.Revoked);
		return UserResult.Ok;
	}

	/// <summary>Replaces this account's password and closes every connection of it.</summary>
	/// <param name="newPasswordHash">The MD5 digest of the new password.</param>
	/// <param name="currentPasswordHash">The MD5 digest of the current password, needed when users change their own.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>Ok, WrongPassword, or why the caller may not.</returns>
	/// <remarks>
	///     An account changes its own password by giving the current one; changing another's needs
	///     <see cref="Permissions.UserManage" /> and outranking them. Every connection, the caller's own included, closes as
	///     <see cref="ConnectionCloseReason.CredentialsChanged" />.
	/// </remarks>
	public async Task<UserResult> ChangePasswordAsync(Md5 newPasswordHash, Md5? currentPasswordHash,
		CancellationToken ct = default)
	{
		if (BasilBot.Is(User.Record)) return UserResult.Protected;

		if (IsSelf)
		{
			if (currentPasswordHash is not { } current || !await VerifyAsync(current, ct)) return UserResult.WrongPassword;
		}
		else if (Guard(Permissions.UserManage, selfAllowed: false) is { } denied)
		{
			return denied;
		}

		await User.Context.Credentials.CreateOrUpdateAsync(new Credentials(User.Record, newPasswordHash), ct);
		User.Context.Sessions.CloseAll(User.Record, ConnectionCloseReason.CredentialsChanged);
		return UserResult.Ok;
	}

	private async Task<bool> VerifyAsync(Md5 passwordHash, CancellationToken ct)
	{
		try
		{
			return await User.Context.Credentials.VerifyAsync(new Credentials(User.Record, passwordHash), ct);
		}
		catch (ArgumentException)
		{
			// The client sent something that is not even a well-formed MD5 digest.
			return false;
		}
	}
}