using Basil.Domain.Sessions;

namespace Basil.Host.Irc.Support;

public static class IrcReplies
{
	public const string Network = "Basil";

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

	public const string ShuttingDown = "Server shutting down";

	public const string NoManagement = "Channels are managed through the API or BasilBot, not through IRC";

	public const string CannotJoin = "Cannot join channel";

	public const string CannotJoinNoAccess = "Cannot join channel (you may not read it)";

	public const string CannotJoinSpectator = "Cannot join channel (spectator channels are managed by the server)";

	public const string CommandFailed = "The command could not be completed. Try again later.";

	public const string EndOfBanList = "End of channel ban list";

	public const string CannotSend = "Cannot send to channel";

	public const string CannotSendNotMember = "Cannot send to channel (you are not in it)";

	public const string CannotSendNoAccess = "Cannot send to channel (you may not write in it)";

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

	public const string EndOfNames = "End of /NAMES list.";

	public const string ListHeader = "Users  FullName";

	public const string EndOfList = "End of /LIST";

	public const string EndOfWho = "End of /WHO list.";

	public const string NoNicknameGiven = "No nickname given";

	public const string EndOfWhoIs = "End of /WHOIS list.";

	public const string IsLoggedInAs = "is logged in as";

	public const string NoTextToSend = "No text to send";

	public const string UsersDontMatch = "Cannot change mode for other users";

	public const string NoMotd = "MOTD File is missing";

	public const string EndOfMotd = "End of /MOTD command.";

	public const string SaslAlready = "You have already authenticated";

	public const string SaslAborted = "SASL authentication aborted";

	public const string SaslFailed = "SASL authentication failed";

	public const string SaslMechanisms = "are available SASL mechanisms";

	public const string SaslSuccess = "SASL authentication successful";

	public const string NoTopic = "No topic is set";

	public const string ConnectionClosed = "Connection closed";

	public const string Supported = "are supported by this server";

	public const string NoLongerAway = "You are no longer marked as being away";

	public const string NowAway = "You have been marked as being away";

	public const string ListChannelColumn = "Channel";

	public const string KickRemoved = "Removed from the channel";

	public const string KickClosed = "The channel was closed";

	public const string KickNoAccess = "You may no longer read the channel";

	public static string NoRecipient(string command)
	{
		return $"No recipient given ({command})";
	}

	public static string Welcome(string mask)
	{
		return $"Welcome to the {Network} IRC network, {mask}";
	}

	public static string YourHost(string serverName, string version)
	{
		return $"Your host is {serverName}, running version {version}";
	}

	public static string Created(DateTimeOffset startedAt)
	{
		return $"This server was started {startedAt:u}";
	}

	public static string MotdStart(string serverName)
	{
		return $"- {serverName} Message of the day -";
	}

	public static string MotdLine(string line)
	{
		return $"- {line}";
	}

	public static string LoggedInAs(string name)
	{
		return $"You are now logged in as {name}";
	}

	public static string ClosingLink(string domain, string reason)
	{
		return $"Closing Link: {domain} ({reason})";
	}

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