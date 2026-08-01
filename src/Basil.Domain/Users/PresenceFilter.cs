namespace Basil.Domain.Users;

/// <summary>
///     Specifies which users a player can see in their presence list.
/// </summary>
public enum PresenceFilter : byte
{
	/// <summary>No presence updates are shown.</summary>
	Nil = 0,
	/// <summary>All online users are shown.</summary>
	All = 1,
	/// <summary>Only friends are shown.</summary>
	Friends = 2
}
