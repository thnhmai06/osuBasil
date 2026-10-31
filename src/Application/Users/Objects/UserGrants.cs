using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

/// <summary>Owns the permissions granted to one account and which of them are in effect.</summary>
public sealed class UserGrants
{
	private readonly User _user;

	/// <summary>Creates the permission state for an account.</summary>
	/// <param name="user">The account that owns the permissions.</param>
	internal UserGrants(User user)
	{
		_user = user;
	}

	/// <summary>Gets the permissions granted to the account.</summary>
	public UserPermissions Granted
	{
		get
		{
			using var scope = _user.Enter();
			return _user.Record.Value.Permissions;
		}
	}

	/// <summary>Gets the granted permissions that no active restriction suspends at a moment.</summary>
	/// <param name="now">The moment to evaluate restrictions at.</param>
	/// <returns>The permissions in effect.</returns>
	public UserPermissions Effective(DateTimeOffset now)
	{
		using var scope = _user.Enter();
		return _user.Record.Value.Permissions.Effective(_user.Restrictions, now);
	}

	/// <summary>Replaces the permissions granted to the account.</summary>
	/// <param name="permissions">The permissions to grant.</param>
	internal void Replace(UserPermissions permissions)
	{
		using var scope = _user.Enter();
		_user.Change(data => data.Permissions = permissions);
	}

	/// <summary>Marks the account as deleted, taking all its permissions away.</summary>
	/// <param name="at">When the account was deleted.</param>
	internal void MarkDeleted(DateTimeOffset at)
	{
		using var scope = _user.Enter();
		_user.Change(data =>
		{
			data.DeletedAt = at;
			data.Permissions = UserPermissions.None;
		});
	}
}