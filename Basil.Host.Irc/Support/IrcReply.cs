using Basil.Application.Shared.Replies;

namespace Basil.Host.Irc.Support;

/// <summary>Every text the IRC host sends, each with its default wording; the operator can reword them in <c>Data/Localizations/irc.json</c>.</summary>
public enum IrcReply
{
	[Reply("You are sending messages too fast. Slow down.")]
	ChatTooFast,

	[Reply("Password incorrect")]
	BadCredentials,

	[Reply("Too many failed login attempts. Try again later.")]
	TooManyAttempts,

	[Reply("Excess flood.")]
	Flooding,

	[Reply("The server is busy. Try again later.")]
	ServerBusy,

	[Reply("This account is already signed in elsewhere.")]
	AlreadyOnline,

	[Reply("Registration timed out.")]
	RegistrationTimeout,

	[Reply("The server is restarting. Reconnect in a moment.")]
	ServerStopping,

	[Reply("Server is shutting down")]
	ShuttingDownNotice,

	[Reply("Server shutting down")]
	ShuttingDown,

	[Reply("Channels are managed through the API or BasilBot, not through IRC")]
	NoManagement,

	[Reply("Cannot join channel")]
	CannotJoin,

	[Reply("Cannot join channel (you may not read it)")]
	CannotJoinNoAccess,

	[Reply("Cannot join channel (spectator channels are managed by the server)")]
	CannotJoinSpectator,

	[Reply("The command could not be completed. Try again later.")]
	CommandFailed,

	[Reply("End of channel ban list")]
	EndOfBanList,

	[Reply("Cannot send to channel")]
	CannotSend,

	[Reply("Cannot send to channel (you are not in it)")]
	CannotSendNotMember,

	[Reply("Cannot send to channel (you may not write in it)")]
	CannotSendNoAccess,

	[Reply("You are not allowed to chat")]
	Silenced,

	[Reply("This user does not accept your messages")]
	DmBlocked,

	[Reply("This user is silenced")]
	TargetSilenced,

	[Reply("No such nick")]
	NoSuchNick,

	[Reply("No such channel")]
	NoSuchChannel,

	[Reply("You are not on that channel")]
	NotOnChannel,

	[Reply("You may not reregister")]
	AlreadyRegistered,

	[Reply("You have not registered")]
	NotRegistered,

	[Reply("Unknown command")]
	UnknownCommand,

	[Reply("Not enough parameters")]
	NeedMoreParams,

	[Reply("Input line was too long")]
	InputTooLong,

	[Reply("End of /NAMES list.")]
	EndOfNames,

	[Reply("Users  FullName")]
	ListHeader,

	[Reply("End of /LIST")]
	EndOfList,

	[Reply("End of /WHO list.")]
	EndOfWho,

	[Reply("No nickname given")]
	NoNicknameGiven,

	[Reply("End of /WHOIS list.")]
	EndOfWhoIs,

	[Reply("is logged in as")]
	IsLoggedInAs,

	[Reply("No text to send")]
	NoTextToSend,

	[Reply("Cannot change mode for other users")]
	UsersDontMatch,

	[Reply("MOTD File is missing")]
	NoMotd,

	[Reply("End of /MOTD command.")]
	EndOfMotd,

	[Reply("You have already authenticated")]
	SaslAlready,

	[Reply("SASL authentication aborted")]
	SaslAborted,

	[Reply("SASL authentication failed")]
	SaslFailed,

	[Reply("are available SASL mechanisms")]
	SaslMechanisms,

	[Reply("SASL authentication successful")]
	SaslSuccess,

	[Reply("No topic is set")]
	NoTopic,

	[Reply("Connection closed")]
	ConnectionClosed,

	[Reply("are supported by this server")]
	Supported,

	[Reply("You are no longer marked as being away")]
	NoLongerAway,

	[Reply("You have been marked as being away")]
	NowAway,

	[Reply("Channel")]
	ListChannelColumn,

	[Reply("Removed from the channel")]
	KickRemoved,

	[Reply("The channel was closed")]
	KickClosed,

	[Reply("You may no longer read the channel")]
	KickNoAccess,

	[Reply("No recipient given ({command})", "command")]
	NoRecipient,

	[Reply("Welcome to the {network} IRC network, {mask}", "network", "mask")]
	Welcome,

	[Reply("Your host is {serverName}, running version {version}", "serverName", "version")]
	YourHost,

	[Reply("This server was started {startedAt}", "startedAt")]
	Created,

	[Reply("- {serverName} Message of the day -", "serverName")]
	MotdStart,

	[Reply("- {line}", "line")]
	MotdLine,

	[Reply("You are now logged in as {name}", "name")]
	LoggedInAs,

	[Reply("Closing Link: {domain} ({reason})", "domain", "reason")]
	ClosingLink,

	[Reply("Quit")]
	ClosingQuit,

	[Reply("Connection lost")]
	ClosingConnectionLost,

	[Reply("Ping timeout")]
	ClosingTimedOut,

	[Reply("Replaced by a new connection")]
	ClosingReplaced,

	[Reply("Key revoked")]
	ClosingRevoked,

	[Reply("Account deleted")]
	ClosingDeleted,

	[Reply("Connection closed")]
	ClosingOther,
}
