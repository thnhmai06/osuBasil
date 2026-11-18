using Basil.Application.Users.Objects.Events;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

public sealed class UserGrants
{
	private readonly User _user;

	internal UserGrants(User user)
	{
		_user = user;
	}

	public UserPrivileges Granted
	{
		get
		{
			using var scope = _user.Enter();
			return _user.Record.Value.Privileges;
		}
	}

	public UserPrivileges Effective(DateTimeOffset now)
	{
		return Granted;
	}

	internal void Replace(UserPrivileges privileges)
	{
		using var scope = _user.Enter();
		_user.Change(data => data.Privileges = privileges);
	}

	internal async Task<UserOutcome> SetAsync(UserPrivileges privileges)
	{
		await _user.StoreChangeAsync(data => data.Privileges = privileges);
		Replace(privileges);
		if (_user.Context.Registry.Online.GetValueOrDefault(_user.Record) is { } user)
			_user.Context.Registry.CloseDisallowed(user);

		_user.Context.Events.Publish(new UserPrivilegesChanged(_user.Record, privileges));
		return UserOutcome.Ok;
	}

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