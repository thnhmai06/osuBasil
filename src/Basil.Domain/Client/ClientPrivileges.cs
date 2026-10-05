namespace Basil.Domain.Client;

/// <summary>
///     Represents the privileges the server reports to an osu! client about the current user.
/// </summary>
/// <remarks>
///     A bitwise combination of flags derived from the user's effective permissions; see <see cref="Basil.Domain.Users.PermissionsExtensions.ToClientPrivileges" />.
/// </remarks>
[Flags]
public enum ClientPrivileges : byte
{
	/// <summary>Indicates that no privileges have been granted to the client.</summary>
	None = 0,

	/// <summary>Marks the client as a regular player.</summary>
	Player = 1 << 0,

	/// <summary>Marks the client as a moderator.</summary>
	Moderator = 1 << 1,

	/// <summary>Marks the client as a supporter.</summary>
	Supporter = 1 << 2,

	/// <summary>Marks the client as the server owner.</summary>
	Owner = 1 << 3,

	/// <summary>Marks the client as a developer.</summary>
	Developer = 1 << 4,

	/// <summary>The user is a tournament staff member.</summary>
	Tournament = 1 << 5
}