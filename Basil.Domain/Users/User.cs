namespace Basil.Domain.Users;

public sealed class User
{
	private readonly Lock _sync = new();

	public User(UserRecord record)
	{
		Record = record;
		Profile = new UserProfile(this);
		Silence = new UserSilence(this);
	}

	public UserRecord Record { get; }

	public UserProfile Profile { get; }

	public UserSilence Silence { get; }

	public UserPrivileges Privileges
	{
		get
		{
			using var scope = Enter();
			return Record.Value.Privileges;
		}
	}

	public void ReplacePrivileges(UserPrivileges privileges)
	{
		using var scope = Enter();
		Record.Value.Privileges = privileges;
	}

	public void MarkDeleted(DateTimeOffset at)
	{
		using var scope = Enter();
		Record.Value.DeletedAt = at;
		Record.Value.Privileges = UserPrivileges.None;
	}

	public Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}
}