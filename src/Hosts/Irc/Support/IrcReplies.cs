using Basil.Domain.Auth;

namespace Basil.Host.Irc.Support;

/// <summary>The texts an IRC client reads and the limits that shape what it may send.</summary>
public static class IrcReplies
{
	/// <summary>The most characters of a chat message the server accepts from an IRC client.</summary>
	public const int MaxMessageLength = 450;

	/// <summary>The most bytes of a line without its tags.</summary>
	public const int MaxBodyBytes = 512;

	/// <summary>The most bytes of a line with its tags.</summary>
	public const int MaxLineBytes = 8191 + MaxBodyBytes;

	/// <summary>The most bytes of message text the server puts on one delivered line.</summary>
	public const int MaxChunkBytes = 400;

	/// <summary>The name of the network, advertised to clients.</summary>
	public const string Network = "Basil";

	/// <summary>The version the server reports.</summary>
	public const string Version = "basil-1";

	/// <summary>Shown when a client chats faster than the server allows.</summary>
	public const string ChatTooFast = "You are sending messages too fast. Slow down.";

	/// <summary>The standard-reply code for <see cref="ChatTooFast" />.</summary>
	public const string RateLimitedCode = "RATE_LIMITED";

	/// <summary>Reply to a failed registration.</summary>
	public const string BadCredentials = "Password incorrect";

	/// <summary>Closing text when the address has too many failed logins.</summary>
	public const string TooManyAttempts = "Too many failed login attempts. Try again later.";

	/// <summary>Closing text when the address has too many connections.</summary>
	public const string TooManyConnections = "Too many connections from your address.";

	/// <summary>Closing text when the key is already used by another socket.</summary>
	public const string KeyInUse = "This key is already connected.";

	/// <summary>Closing text when the client did not register in time.</summary>
	public const string RegistrationTimeout = "Registration timed out.";

	/// <summary>Closing text when the server is stopping.</summary>
	public const string ServerStopping = "The server is restarting. Please reconnect in a moment.";

	/// <summary>Notice sent to every client when the server shuts down.</summary>
	public const string ShuttingDownNotice = "Server is shutting down";

	/// <summary>Notice sent to every client when the server restarts.</summary>
	public const string RestartingNotice = "Server is restarting";

	/// <summary>Closing reason on shutdown.</summary>
	public const string ShuttingDown = "Server shutting down";

	/// <summary>Closing reason on restart.</summary>
	public const string Restarting = "Server restarting";

	/// <summary>Reply to a command that changes a channel, a user or the network.</summary>
	public const string NoManagement = "Channels are managed through the API or BasilBot, not through IRC";

	/// <summary>Reply to a join the channel's read rules refuse.</summary>
	public const string CannotJoin = "Cannot join channel";

	/// <summary>Reply to a message the channel's write rules refuse.</summary>
	public const string CannotSend = "Cannot send to channel";

	/// <summary>Reply when the sender lacks the permission to chat.</summary>
	public const string Silenced = "You are not allowed to chat";

	/// <summary>Reply when the recipient does not take messages from the sender.</summary>
	public const string DmBlocked = "This user does not accept your messages";

	/// <summary>Reply when the recipient is not allowed to chat.</summary>
	public const string TargetSilenced = "This user is silenced";

	/// <summary>Reply to a nickname that is not online.</summary>
	public const string NoSuchNick = "No such nick";

	/// <summary>Reply to a channel that does not exist.</summary>
	public const string NoSuchChannel = "No such channel";

	/// <summary>Reply to a channel the client is not on.</summary>
	public const string NotOnChannel = "You're not on that channel";

	/// <summary>Reply to a command that only works before registration.</summary>
	public const string AlreadyRegistered = "You may not reregister";

	/// <summary>Reply to a command before registration.</summary>
	public const string NotRegistered = "You have not registered";

	/// <summary>Reply to an unknown command.</summary>
	public const string UnknownCommand = "Unknown command";

	/// <summary>Reply to a command that lacks parameters.</summary>
	public const string NeedMoreParams = "Not enough parameters";

	/// <summary>Reply to a line that is too long.</summary>
	public const string InputTooLong = "Input line was too long";

	/// <summary>Gets the text shown to clients when a connection ends for a reason.</summary>
	/// <param name="reason">Why the connection ended.</param>
	/// <returns>The wording for <c>QUIT</c> and <c>ERROR</c>.</returns>
	public static string Closing(SessionEndReason reason)
	{
		return reason switch
		{
			SessionEndReason.LoggedOut => "Quit",
			SessionEndReason.ConnectionLost => "Connection lost",
			SessionEndReason.TimedOut => "Ping timeout",
			SessionEndReason.Replaced => "Replaced by a new connection",
			SessionEndReason.Revoked or SessionEndReason.CredentialsChanged => "Key revoked",
			SessionEndReason.Deleted => "Account deleted",
			_ => "Connection closed"
		};
	}
}
