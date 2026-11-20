namespace Basil.Domain.Users;

/// <summary>Which users' presence a user chooses to receive: online status and activity of nobody, everybody or only friends.</summary>
public enum PresenceVisibility : byte
{
	/// <summary>No users.</summary>
	Nil = 0,

	/// <summary>All users.</summary>
	All = 1,

	/// <summary>Only the friends of the user.</summary>
	Friends = 2
}