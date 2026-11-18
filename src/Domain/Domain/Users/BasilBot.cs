namespace Basil.Domain.Users;

public static class BasilBot
{
	public const int Id = 1;

	public const string DefaultName = "BasilBot";

	public static bool IsBasilBot(this UserRecord user)
	{
		return user.Id == Id;
	}
}