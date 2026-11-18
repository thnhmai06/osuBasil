using Basil.Domain.Users;

namespace Basil.Testing.Persistence;

public static class StoredUsers
{
	public static UserRecord Regular { get; } = User("Regular");

	public static UserRecord Renamed => User("Renamed");

	private static UserRecord User(string name)
	{
		return new UserRecord { Id = 8, Value = new UserData { Name = name } };
	}
}