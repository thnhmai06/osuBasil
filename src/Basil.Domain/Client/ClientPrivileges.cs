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

/// <summary>
///     Provides extension methods for working with <see cref="ClientPrivileges" /> flag values.
/// </summary>
public static class ClientPrivilegesExtensions
{
	/// <summary>
	///     Determines whether <paramref name="target" /> has every flag set in
	///     <paramref name="requirement" />.
	/// </summary>
	/// <param name="target">The flags to test.</param>
	/// <param name="requirement">The flags that must all be present in <paramref name="target" />.</param>
	/// <returns>
	///     <see langword="true" /> if all flags in <paramref name="requirement" /> are set in
	///     <paramref name="target" />; otherwise, <see langword="false" />.
	/// </returns>
	public static bool Has(this ClientPrivileges target, ClientPrivileges requirement)
	{
		return (target & requirement) == requirement;
	}
}