namespace Basil.Domain.Users;

/// <summary>Who may see that a user is online and what the user is doing.</summary>
public enum PresenceVisibility : byte
{
	/// <summary>Nobody.</summary>
	Nil = 0,

	/// <summary>Every user.</summary>
	All = 1,

	/// <summary>Only the user's friends.</summary>
	Friends = 2
}