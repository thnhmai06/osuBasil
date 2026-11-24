namespace Basil.Host.Stable.Support;

public static class StableReplies
{
	public const int MaxMessageLength = 450;

	public static string InviteText(string link)
	{
		return $"Come join my game: {link}";
	}

	public const string ChatTooFast = "You are sending messages too fast. Slow down.";

	public const string AlreadyOnline = "User already logged in.";

	public const string TooManyAttempts = "Too many failed login attempts. Try again later.";

	public const string NoTourneyPrivilege = "This account may not use the osu!tourney client.";

	public const string TooManyConnections = "Too many connections from your address.";

	public const string ServerStopping = "The server is restarting. Please reconnect in a moment.";
}