namespace Basil.Domain.Users;

/// <summary>The fixed user that is the server's voice in chat; the server speaks through it, protects it, and nobody can sign in as it or act for it.</summary>
public static class BasilBot
{
	/// <summary>The id of the fixed user that is the server's voice in chat.</summary>
	public const int Id = 1;

	/// <summary>Determines whether an account is BasilBot.</summary>
	/// <param name="user">The account to check.</param>
	/// <returns><see langword="true" /> when the account has BasilBot's fixed id.</returns>
	public static bool Is(UserRecord user)
	{
		return user.Id == Id;
	}
}