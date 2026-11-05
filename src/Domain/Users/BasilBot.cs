namespace Basil.Domain.Users;

/// <summary>
///     The fixed user that is the server's voice in chat; the server speaks through it, protects it, and nobody can
///     sign in as it or act for it.
/// </summary>
public static class BasilBot
{
	/// <summary>The id of the fixed user that is the server's voice in chat.</summary>
	public const int Id = 1;

	/// <summary>The name the bot gets when the server creates or restores its account.</summary>
	public const string DefaultName = "BasilBot";

	/// <summary>Determines whether an account is BasilBot.</summary>
	/// <param name="user">The account to check.</param>
	/// <returns><see langword="true" /> when the account has BasilBot's fixed id.</returns>
	public static bool IsBasilBot(this UserRecord user)
	{
		return user.Id == Id;
	}

	/// <summary>
	///     Determines whether an account stands in the friend relationship with every account: it blocks nobody, may
	///     always be messaged, and cannot be blocked or unfriended. This holds for BasilBot only.
	/// </summary>
	/// <param name="user">The account to check.</param>
	/// <returns><see langword="true" /> when the account is a friend of everyone.</returns>
	public static bool IsFriendOfEveryone(this UserRecord user)
	{
		return user.IsBasilBot();
	}
}