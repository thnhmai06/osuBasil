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

	/// <summary>The account already holds as many open connections of the same kind as the server allows.</summary>
	TooManySessions,

	/// <summary>The account may not connect with an osu!tourney client.</summary>
	NoTourneyPrivilege,

	/// <summary>The token or key matches no active session.</summary>
	InvalidToken
}