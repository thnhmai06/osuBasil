namespace Basil.Protocol;

/// <summary>Specifies why a login attempt was rejected, sent as the user id value of the login reply packet on failure.</summary>
public enum LoginFailureReason
{
	/// <summary>The supplied credentials did not match a valid account.</summary>
	AuthenticationFailed = -1,

	/// <summary>The client version is too old to be supported.</summary>
	OldClient = -2,

	/// <summary>The account is banned or restricted.</summary>
	Banned = -3,

	/// <summary>An internal error occurred while processing the login.</summary>
	ErrorOccurred = -5,

	/// <summary>The account is required to have supporter status.</summary>
	NeedsSupporter = -6,

	/// <summary>The account password has been reset and must be changed.</summary>
	PasswordReset = -7,

	/// <summary>The account requires email verification before it can log in.</summary>
	RequiresVerification = -8
}