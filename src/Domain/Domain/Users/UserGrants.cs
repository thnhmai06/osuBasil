using Basil.Domain.Game.Users;

namespace Basil.Domain.Users;

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

	public void Replace(UserPrivileges privileges)
	{
		using var scope = _user.Enter();
		_user.Record.Value.Privileges = privileges;
	}

	public void MarkDeleted(DateTimeOffset at)
	{
		using var scope = _user.Enter();
		_user.Record.Value.DeletedAt = at;
		_user.Record.Value.Privileges = UserPrivileges.None;
	}
}
