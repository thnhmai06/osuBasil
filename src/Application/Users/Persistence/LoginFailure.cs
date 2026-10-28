namespace Basil.Application.Users.Persistence;

/// <summary>The reasons a login attempt can fail.</summary>
public enum LoginFailure : byte
{
	/// <summary>The username does not match a registered account.</summary>
	UnknownUser,

	/// <summary>The password did not match the account's stored credential.</summary>
	WrongPassword,

	/// <summary>The account has been deleted.</summary>
	AccountDeleted,

	/// <summary>The account already has an active connection of the same kind.</summary>
	AlreadyOnline,

	/// <summary>The account may not connect with an osu!tourney client.</summary>
	NoTourneyPermission,

	/// <summary>The device was logged out remotely and must show its login form once before it signs in again.</summary>
	PasswordResetRequired,

	/// <summary>The token or key matches no active session.</summary>
	InvalidToken
}