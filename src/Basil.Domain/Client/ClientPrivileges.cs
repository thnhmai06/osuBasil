namespace Basil.Domain.Client;

/// <summary>
///     Represents the privileges the server reports to an osu! client about the current user.
/// </summary>
/// <remarks>
///     A bitwise combination of flags sent to the client during a session.
/// </remarks>
[Flags]
public enum ClientPrivileges : byte
{
	/// <summary>Client can't do anything. :)</summary>
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
	Developer = 1 << 4
}

public static class ClientPrivilegesExtensions
{
	public static bool Has(this ClientPrivileges target, ClientPrivileges requirement)
	{
		return (target & requirement) == requirement;
	}
}