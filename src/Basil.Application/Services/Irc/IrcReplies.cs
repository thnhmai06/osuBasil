namespace Basil.Application.Services.Irc;

/// <summary>
///     The user-visible reply text the IRC gateway sends, the single source of truth for that
///     surface.
/// </summary>
/// <remarks>
///     Every literal an IRC client can read — the registration handshake, query replies, and
///     command-error messages — is a named constant here. Production services emit these constants,
///     and tests assert against the same symbols, so the two cannot drift. Format strings use
///     <see cref="string.Format(string, object?[])" /> placeholders; fixed strings are plain
///     <see cref="string" /> constants. Deliberately changing the wording of a reply is a
///     public-behavior change: a one-line edit to the constant here, and every test that pins it
///     stays in sync.
/// </remarks>
public static class IrcReplies
{
	// ── registration handshake ──────────────────────────────────────────────────────────────
	/// <summary>RPL_WELCOME text; <c>{0}</c> is the server name, <c>{1}</c> the user's nick.</summary>
	public const string Welcome = "Welcome to {0} IRC, {1}";

	/// <summary>RPL_YOURHOST text; <c>{0}</c> is the server name, <c>{1}</c> the version.</summary>
	public const string YourHost = "Your host is {0}, running version {1}";

	/// <summary>RPL_CREATED text; <c>{0}</c> is the server creation timestamp.</summary>
	public const string ServerCreated = "This server was created {0}";

	/// <summary>Trailing parameter of the RPL_ISUPPORT line.</summary>
	public const string AreSupportedByThisServer = "are supported by this server";

	// ── query replies ──────────────────────────────────────────────────────────────────────
	/// <summary>RPL_ENDOFNAMES text.</summary>
	public const string EndOfNames = "End of /NAMES list";

	/// <summary>RPL_ENDOFWHO text.</summary>
	public const string EndOfWho = "End of /WHO list";

	/// <summary>RPL_ENDOFWHOIS text.</summary>
	public const string EndOfWhoIs = "End of /WHOIS list";

	/// <summary>RPL_ENDOFLIST text.</summary>
	public const string EndOfList = "End of /LIST";

	/// <summary>RPL_NOTOPIC text.</summary>
	public const string NoTopicIsSet = "No topic is set";

	/// <summary>ERR_NOSUCHCHANNEL text.</summary>
	public const string NoSuchChannel = "No such channel";

	/// <summary>ERR_NOSUCHNICK text.</summary>
	public const string NoSuchNickChannel = "No such nick/channel";

	/// <summary>The server label reported in RPL_WHOISSERVER and RPL_VERSION.</summary>
	public const string IrcGateway = "Basil IRC gateway";

	/// <summary>Trailing parameter of the RPL_WHOISIDLE line.</summary>
	public const string SecondsIdleSignonTime = "seconds idle, signon time";

	/// <summary>ERR_CHANOPRIVSNEEDED text when a topic change is refused.</summary>
	public const string TopicManagedByServer = "Channel topics are managed by the server";

	/// <summary>ERR_CHANOPRIVSNEEDED text when a mode change is refused.</summary>
	public const string ModesManagedByServer = "Channel modes are managed by the server";

	// ── MOTD ───────────────────────────────────────────────────────────────────────────────
	/// <summary>ERR_NOMOTD text.</summary>
	public const string NoMotd = "The server has no MOTD.";

	/// <summary>RPL_MOTDSTART text; <c>{0}</c> is the server name.</summary>
	public const string MotdStart = "- {0} Message of the Day -";

	/// <summary>RPL_ENDOFMOTD text.</summary>
	public const string EndOfMotd = "End of /MOTD command.";

	// ── LUSERS ─────────────────────────────────────────────────────────────────────────────
	/// <summary>RPL_LUSERCLIENT text; <c>{0}</c> is the account count.</summary>
	public const string LusersClients = "There are {0} users and 0 invisible on 1 servers";

	/// <summary>RPL_LUSERCHANNELS trailing parameter.</summary>
	public const string ChannelsFormed = "channels formed";

	/// <summary>RPL_LUSERME text; <c>{0}</c> is the client count.</summary>
	public const string LusersMe = "I have {0} clients and 1 servers";

	// ── LIST / NAMES headers ───────────────────────────────────────────────────────────────
	/// <summary>RPL_LISTSTART column header for the channel column.</summary>
	public const string ListChannel = "Channel";

	/// <summary>RPL_LISTSTART column header for the user-count column.</summary>
	public const string ListUsers = "Users  Name";

	// ── authentication and command errors ──────────────────────────────────────────────────
	/// <summary>ERR_PASSWDMISMATCH text.</summary>
	public const string PasswordIncorrect = "Password incorrect";

	/// <summary>ERR_NICKNAMEINUSE text.</summary>
	public const string NicknameInUse = "Nickname is already in use";

	/// <summary>ERR_ERRONEUSNICKNAME text.</summary>
	public const string ErroneousNickname = "Erroneous nickname";

	/// <summary>ERR_NOTREGISTERED text.</summary>
	public const string YouHaveNotRegistered = "You have not registered";

	/// <summary>ERR_NEEDMOREPARAMS text.</summary>
	public const string NotEnoughParameters = "Not enough parameters";

	/// <summary>ERR_INVITEONLYCHAN text.</summary>
	public const string CannotJoinChannel = "Cannot join channel (no permission)";

	/// <summary>ERR_ALREADYREGISTERED text.</summary>
	public const string NicknameChangeNotSupported = "Changing nickname is not supported";

	/// <summary>ERR_UNKNOWNCOMMAND text.</summary>
	public const string UnknownCommand = "Unknown command";

	/// <summary>ERR_CANNOTSENDTOCHAN text.</summary>
	public const string CannotSendToChannel = "Cannot send to channel";
}
