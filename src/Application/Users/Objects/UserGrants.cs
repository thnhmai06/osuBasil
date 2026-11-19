using Basil.Application.Users.Objects.Events;
using Basil.Domain.Game.Users;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

public sealed class UserGrants
{
	private readonly User _user;

	internal UserGrants(User user)
	{
		_user = user;
	}

	public UserPrivileges Granted => _user.State.Grants.Granted;

	public UserPrivileges Effective(DateTimeOffset now)
	{
		return _user.State.Grants.Effective(now);
	}

	internal void Replace(UserPrivileges privileges)
	{
		_user.Apply(user => user.Grants.Replace(privileges));
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
		_user.Apply(user => user.Grants.MarkDeleted(at));
	}
}