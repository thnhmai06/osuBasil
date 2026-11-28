namespace Basil.Domain.Users;

/// <summary>Whether an account may sign in at all, before its password is checked.</summary>
public enum UserSignInResult : byte
{
	/// <summary>The account never signs in: it is the server's own user.</summary>
	Denied = 0,

	/// <summary>The account may sign in.</summary>
	Ok = 1,

	/// <summary>The account was deleted.</summary>
	AccountDeleted = 2
}
