namespace Basil.Domain.Users;

public sealed class User
{
	private readonly Lock _sync = new();

	public User(UserRecord record)
	{
		Record = record;
		Grants = new UserGrants(this);
		Profile = new UserProfile(this);
		Silence = new UserSilence(this);
	}

	public UserRecord Record { get; }

	public UserGrants Grants { get; }

	public UserProfile Profile { get; }

	public UserSilence Silence { get; }

	public Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}
}
