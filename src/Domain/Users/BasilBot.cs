namespace Basil.Domain.Users;

/// <summary>The fixed user that is the server's voice in chat; it signs in nowhere and holds no permission.</summary>
public static class BasilBot
{
	/// <summary>The id of the fixed user that is the server's voice in chat.</summary>
	public const int Id = 1;

	/// <summary>The name of the fixed user that is the server's voice in chat.</summary>
	public const string Name = "BasilBot";
}
