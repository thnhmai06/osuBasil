using Basil.Domain.Users;

namespace Basil.Infrastructure.Persistence.Tests.Support;

/// <summary>Builds the user records the file storage tests work on.</summary>
internal static class StoredUsers
{
	public static UserRecord Regular { get; } = User("Regular");

	public static UserRecord Renamed => User("Renamed");

	/// <summary>Creates a user of the given name with the given id.</summary>
	private static UserRecord User(string name)
	{
		return new UserRecord { Id = 8, Value = new UserData { Name = name } };
	}
}
