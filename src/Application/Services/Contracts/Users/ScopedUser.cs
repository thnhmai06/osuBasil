using Basil.Application.Services.Contracts.Sessions;
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
}
