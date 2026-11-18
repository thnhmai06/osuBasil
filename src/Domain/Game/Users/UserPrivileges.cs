namespace Basil.Domain.Game.Users;

[Flags]
public enum UserPrivileges : byte
{
	None = 0,

	Player = 1 << 0,

	Moderator = 1 << 1,

	Supporter = 1 << 2,

	Owner = 1 << 3,

	Developer = 1 << 4,

	Tournament = 1 << 5
}

public static class UserPrivilegesExtensions
{
	public const UserPrivileges All = UserPrivileges.Player | UserPrivileges.Moderator | UserPrivileges.Supporter |
	                                  UserPrivileges.Owner | UserPrivileges.Developer | UserPrivileges.Tournament;

	public const UserPrivileges Default = UserPrivileges.Player | UserPrivileges.Supporter;

	extension(UserPrivileges held)
	{
		public bool Allows(UserPrivileges required)
		{
			return (held & required) == required;
		}

		public UserPrivileges Completed()
		{
			return held.Allows(UserPrivileges.Tournament) ? held | UserPrivileges.Supporter : held;
		}
	}
}