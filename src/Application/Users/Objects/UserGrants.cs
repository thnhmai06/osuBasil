using Basil.Application.Users.Objects.Events;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

/// <summary>Owns the privileges granted to one account and which of them are in effect.</summary>
public sealed class UserGrants
{
	private readonly User _user;

	/// <summary>Creates the privilege state for an account.</summary>
	/// <param name="user">The account that owns the privileges.</param>
	internal UserGrants(User user)
	{
		_user = user;
	}

	/// <summary>Gets the privileges granted to the account.</summary>
	public UserPrivileges Granted
	{
		get
		{
			using var scope = _user.Enter();
			return _user.Record.Value.Privileges;
		}
	}

	/// <summary>Gets the granted privileges in effect at a moment.</summary>
	/// <param name="now">The moment to evaluate at.</param>
	/// <returns>The granted privileges.</returns>
	public UserPrivileges Effective(DateTimeOffset now)
	{
		return Granted;
	}

	/// <summary>Replaces the privileges granted to the account.</summary>
	/// <param name="privileges">The privileges to grant.</param>
	internal void Replace(UserPrivileges privileges)
	{
		using var scope = _user.Enter();
		_user.Change(data => data.Privileges = privileges);
	}

	/// <summary>
	///     Replaces the privileges granted to the account, signs out sessions that no longer have the right, and
	///     announces it.
	/// </summary>
	/// <param name="privileges">The privileges to grant.</param>
	/// <returns>The outcome of replacing the privileges.</returns>
	internal async Task<UserOutcome> SetAsync(UserPrivileges privileges)
	{
		await _user.StoreChangeAsync(data => data.Privileges = privileges);
		Replace(privileges);
		if (_user.Context.Registry.Online.GetValueOrDefault(_user.Record) is { } user)
			_user.Context.Registry.CloseDisallowed(user);

		_user.Context.Events.Publish(new UserPrivilegesChanged(_user.Record, privileges));
		return UserOutcome.Ok;
	}

	/// <summary>Marks the account as deleted, taking all its privileges away.</summary>
	/// <param name="at">When the account was deleted.</param>
	internal void MarkDeleted(DateTimeOffset at)
	{
		using var scope = _user.Enter();
		_user.Change(data =>
		{
			data.DeletedAt = at;
			data.Privileges = UserPrivileges.None;
		});
	}
}