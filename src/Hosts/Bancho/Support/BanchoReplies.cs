namespace Basil.Host.Bancho.Support;

/// <summary>The texts an osu! client reads and the limits that shape what it may send.</summary>
public static class BanchoReplies
{
	/// <summary>The most characters of a chat message the server accepts from an osu! client.</summary>
	public const int MaxMessageLength = 450;

	/// <summary>Shown when a client chats faster than the server allows.</summary>
	public const string ChatTooFast = "You are sending messages too fast. Slow down.";

	/// <summary>Shown when login is refused because the account is already online.</summary>
	public const string AlreadyOnline = "This account is already online.";

	/// <summary>Shown when login is refused because of too many failed attempts.</summary>
	public const string TooManyAttempts = "Too many failed login attempts. Try again later.";

	/// <summary>Shown when an osu!tourney client lacks the observer permission.</summary>
	public const string NoTourneyPermission = "This account may not use the osu!tourney client.";

	/// <summary>Shown when login is refused because the address has too many connections.</summary>
	public const string TooManyConnections = "Too many connections from your address.";

	/// <summary>Shown when the server is shutting down.</summary>
	public const string ServerStopping = "The server is restarting. Please reconnect in a moment.";
}