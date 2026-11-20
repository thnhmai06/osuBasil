namespace Basil.Domain.Users;

/// <summary>The privileges of an account; each flag owns a set of actions and nothing implies another flag.</summary>
[Flags]
public enum UserPrivileges : byte
{
	/// <summary>No privilege.</summary>
	None = 0,

	/// <summary>Lets the holder play, chat, create and join rooms, spectate, and submit scores and uploads.</summary>
	Player = 1 << 0,

	/// <summary>Lets the holder silence accounts, add notes, read sessions and login history, message anyone, send announcements and manage chat channels.</summary>
	Moderator = 1 << 1,

	/// <summary>Lets the holder download beatmapsets from the game client.</summary>
	Supporter = 1 << 2,

	/// <summary>Lets the holder manage accounts, set privileges and manage server content.</summary>
	Owner = 1 << 3,

	/// <summary>Lets the holder change the server settings, read diagnostics, reload beatmaps and calculate the pp of stored scores.</summary>
	Developer = 1 << 4,

	/// <summary>Lets the holder manage and watch any room and create rooms past the creation limit.</summary>
	Tournament = 1 << 5
}

/// <summary>Helpers for combining and checking <see cref="UserPrivileges" />.</summary>
public static class UserPrivilegesExtensions
{
	/// <summary>Every defined privilege.</summary>
	public const UserPrivileges All = UserPrivileges.Player | UserPrivileges.Moderator | UserPrivileges.Supporter |
	                                  UserPrivileges.Owner | UserPrivileges.Developer | UserPrivileges.Tournament;

	/// <summary>The privileges a new account holds when none are named.</summary>
	public const UserPrivileges Default = UserPrivileges.Player | UserPrivileges.Supporter;

	extension(UserPrivileges held)
	{
		/// <summary>Checks that the held privileges include every privilege required; an empty requirement is always met.</summary>
		/// <param name="required">The privileges the action needs.</param>
		/// <returns><see langword="true" /> when every required privilege is held.</returns>
		public bool Allows(UserPrivileges required)
		{
			return (held & required) == required;
		}

		/// <summary>Gets the held privileges with the ones that always come with them added: <see cref="UserPrivileges.Tournament" /> brings <see cref="UserPrivileges.Supporter" />.</summary>
		/// <returns>The completed privileges.</returns>
		public UserPrivileges Completed()
		{
			return held.Allows(UserPrivileges.Tournament) ? held | UserPrivileges.Supporter : held;
		}
	}
}