using Basil.Domain.Authentication;

namespace Basil.Host.Irc.Support;

public static class IrcReplies
{
	public const int MaxMessageLength = 450;

	public const int MaxBodyBytes = 512;

	public const int MaxLineBytes = 8191 + MaxBodyBytes;

	public const int MaxChunkBytes = 400;

	public const string Network = "Basil";

	public const string Version = "basil-1";

	public const string ChatTooFast = "You are sending messages too fast. Slow down.";

	public const string RateLimitedCode = "RATE_LIMITED";

	public const string BadCredentials = "Password incorrect";

	public const string TooManyAttempts = "Too many failed login attempts. Try again later.";

	public const string TooManyConnections = "Too many connections from your address.";

	public const string Flooding = "Excess flood.";

	public const string ServerBusy = "The server is busy. Try again later.";

	public const string AlreadyOnline = "This account is already signed in elsewhere.";

	public const string RegistrationTimeout = "Registration timed out.";

	public const string ServerStopping = "The server is restarting. Please reconnect in a moment.";

	public const string ShuttingDownNotice = "Server is shutting down";

	public const string RestartingNotice = "Server is restarting";

	public const string ShuttingDown = "Server shutting down";

	public const string Restarting = "Server restarting";

	public const string NoManagement = "Channels are managed through the API or BasilBot, not through IRC";

	public const string CannotJoin = "Cannot join channel";

	public const string CannotSend = "Cannot send to channel";

	public const string Silenced = "You are not allowed to chat";

	public const string DmBlocked = "This user does not accept your messages";

	public const string TargetSilenced = "This user is silenced";

	public const string NoSuchNick = "No such nick";

	public const string NoSuchChannel = "No such channel";

	public const string NotOnChannel = "You're not on that channel";

	public const string AlreadyRegistered = "You may not reregister";

	public const string NotRegistered = "You have not registered";

	public const string UnknownCommand = "Unknown command";

	public const string NeedMoreParams = "Not enough parameters";

	public const string InputTooLong = "Input line was too long";

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