namespace Basil.Domain.Users;

/// <summary>The rule a new password meets wherever an account gets one; a password already stored is never checked again.</summary>
public static class Password
{
	/// <summary>The fewest characters a new password may have.</summary>
	public const int MinLength = 8;

	/// <summary>The sentence that names the rule to whoever chose a password that breaks it.</summary>
	public const string TooShort = "The password needs at least 8 characters.";

	/// <summary>Whether a password, as typed, may be given to an account.</summary>
	public static bool IsAcceptable(string password)
	{
		return password.Length >= MinLength;
	}
}
