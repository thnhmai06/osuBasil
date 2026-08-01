namespace Basil.Domain.Users;

/// <summary>
///     Represents the server-side privileges granted to a user.
/// </summary>
/// <remarks>
///     A bitwise combination of flags that control what a user can do on the server.
///     <see cref="Donator" /> and <see cref="Staff" /> group the base privileges they imply.
/// </remarks>
[Flags]
public enum UserPrivileges : ushort
{
	/// <summary>Allows the user to play and use the server normally.</summary>
	Unrestricted = 1 << 0,
	/// <summary>Marks the account as verified.</summary>
	Verified = 1 << 1,
	/// <summary>Marks the user's scores as whitelisted.</summary>
	Whitelisted = 1 << 2,
	/// <summary>Marks the user as a supporter.</summary>
	Supporter = 1 << 4,
	/// <summary>Marks the user as a premium account.</summary>
	Premium = 1 << 5,
	/// <summary>Marks the user as alumni.</summary>
	Alumni = 1 << 7,
	/// <summary>Grants tournament management rights.</summary>
	TourneyManager = 1 << 10,
	/// <summary>Grants beatmap nomination rights.</summary>
	Nominator = 1 << 11,
	/// <summary>Grants moderator rights.</summary>
	Moderator = 1 << 12,
	/// <summary>Grants administrator rights.</summary>
	Administrator = 1 << 13,
	/// <summary>Grants developer rights.</summary>
	Developer = 1 << 14,

	/// <summary>Combines the supporter and premium privileges.</summary>
	Donator = Supporter | Premium,
	/// <summary>Combines the moderator, administrator, and developer privileges.</summary>
	Staff = Moderator | Administrator | Developer
}

/// <summary>
///     Represents the privileges the server reports to an osu! client about the current user.
/// </summary>
/// <remarks>
///     A bitwise combination of flags sent to the client during a session.
/// </remarks>
[Flags]
public enum ClientPrivileges : byte
{
	/// <summary>Marks the user as a regular player.</summary>
	Player = 1 << 0,
	/// <summary>Marks the user as a moderator.</summary>
	Moderator = 1 << 1,
	/// <summary>Marks the user as a supporter.</summary>
	Supporter = 1 << 2,
	/// <summary>Marks the user as the server owner.</summary>
	Owner = 1 << 3,
	/// <summary>Marks the user as a developer.</summary>
	Developer = 1 << 4,
	/// <summary>Marks the user as a tournament client. Not used in communications with the osu! client.</summary>
	Tournament = 1 << 5
}
