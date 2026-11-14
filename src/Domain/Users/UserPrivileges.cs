namespace Basil.Domain.Users;

/// <summary>The privileges an account holds: the roles osu! knows, which are also the privileges the server checks.</summary>
/// <remarks>
///     A bitwise combination. The values are the privilege bits an osu! client reports and receives, so they never
///     change. Privileges are stored on the account and granted by an owner; holding one is what allows its actions.
/// </remarks>
[Flags]
public enum UserPrivileges : byte
{
	/// <summary>No privilege.</summary>
	None = 0,

	/// <summary>
	///     A regular player: plays, chats, joins and creates rooms, spectates, submits scores and uploads beatmapsets
	///     and screenshots.
	/// </summary>
	Player = 1 << 0,

	/// <summary>
	///     A moderator: silences accounts, adds notes, reads sessions, messages anyone, sends announcements, manages
	///     chat channels and screenshots.
	/// </summary>
	Moderator = 1 << 1,

	/// <summary>A supporter: downloads beatmapsets in game; osu!tourney also requires this.</summary>
	Supporter = 1 << 2,

	/// <summary>The server owner: manages accounts, their privileges and the server content.</summary>
	Owner = 1 << 3,

	/// <summary>A developer: manages the server settings and diagnostics.</summary>
	Developer = 1 << 4,

	/// <summary>
	///     A tournament staff member: manages any room, watches rooms, posts in any room and manages beatmapsets.
	/// </summary>
	Tournament = 1 << 5
}

/// <summary>Provides the rules for combining <see cref="UserPrivileges" />.</summary>
public static class UserPrivilegesExtensions
{
	/// <summary>Every privilege the server defines.</summary>
	public const UserPrivileges All = UserPrivileges.Player | UserPrivileges.Moderator | UserPrivileges.Supporter |
	                                  UserPrivileges.Owner | UserPrivileges.Developer | UserPrivileges.Tournament;

	/// <summary>The privileges a new account holds.</summary>
	public const UserPrivileges Default = UserPrivileges.Player | UserPrivileges.Supporter;

	/// <param name="held">The privileges held.</param>
	extension(UserPrivileges held)
	{
		/// <summary>Determines whether every privilege of a requirement is held.</summary>
		/// <param name="required">The privileges that must all be held; none is always satisfied.</param>
		/// <returns><see langword="true" /> if every privilege in <paramref name="required" /> is held.</returns>
		public bool Allows(UserPrivileges required)
		{
			return (held & required) == required;
		}

		/// <summary>Adds the privileges that another privilege needs to work.</summary>
		/// <returns>
		///     The privileges with <see cref="UserPrivileges.Supporter" /> added when
		///     <see cref="UserPrivileges.Tournament" /> is held, because osu!tourney requires it.
		/// </returns>
		public UserPrivileges Completed()
		{
			return held.Allows(UserPrivileges.Tournament) ? held | UserPrivileges.Supporter : held;
		}
	}
}