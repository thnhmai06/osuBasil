namespace Basil.Host.Stable.Support;

public static class StableReplies
{
	public const int MaxMessageLength = 450;

	public const string ChatTooFast = "You are sending messages too fast. Slow down.";

	public const string TooManySessions = "This account has too many open sessions.";

	public const string TooManyAttempts = "Too many failed login attempts. Try again later.";

	public const string NoTourneyPrivilege = "This account may not use the osu!tourney client.";

	public const string TooManyConnections = "Too many connections from your address.";

	public const string ServerStopping = "The server is restarting. Please reconnect in a moment.";
}