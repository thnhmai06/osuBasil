namespace Basil.Application.Services.Irc;

/// <summary>
///     The user-visible reply text the IRC gateway sends, the single source of truth for that
///     surface.
/// </summary>
/// <remarks>
///     Every literal an IRC client can read — the registration handshake, query replies, and
///     command-error messages — is a named member here. Production services read these members,
///     and tests assert against the same symbols, so the two cannot drift. The wording itself lives
///     outside the code, in <see cref="ReplyLocale" />'s <c>Irc.json</c>, so it can be edited without
///     a rebuild; each member's name, plus the <c>// ──</c> comment group it sits under, is the
///     file's <c>Category.Member</c> lookup key (via <see cref="ReplyLocale.Irc" />), so a member and
///     its wording cannot silently fall out of sync. Format strings use
///     <see cref="string.Format(string, object?[])" /> placeholders; fixed strings are plain text.
///     Deliberately changing the wording of a reply is a public-behavior change: an edit to the
///     locale file, and every test that pins it stays in sync.
/// </remarks>
public static class IrcReplies
{
	// ── registration handshake ──────────────────────────────────────────────────────────────
	/// <summary>RPL_WELCOME text; <c>{0}</c> is the server name, <c>{1}</c> the user's nick.</summary>
	public static readonly string Welcome = ReplyLocale.Irc($"Registration.{nameof(Welcome)}");

	/// <summary>RPL_YOURHOST text; <c>{0}</c> is the server name, <c>{1}</c> the version.</summary>
	public static readonly string YourHost = ReplyLocale.Irc($"Registration.{nameof(YourHost)}");

	/// <summary>RPL_CREATED text; <c>{0}</c> is the server creation timestamp.</summary>
	public static readonly string ServerCreated = ReplyLocale.Irc($"Registration.{nameof(ServerCreated)}");

	/// <summary>Trailing parameter of the RPL_ISUPPORT line.</summary>
	public static readonly string AreSupportedByThisServer = ReplyLocale.Irc($"Registration.{nameof(AreSupportedByThisServer)}");

	// ── query replies ──────────────────────────────────────────────────────────────────────
	/// <summary>RPL_ENDOFNAMES text.</summary>
	public static readonly string EndOfNames = ReplyLocale.Irc($"Query.{nameof(EndOfNames)}");

	/// <summary>RPL_ENDOFWHO text.</summary>
	public static readonly string EndOfWho = ReplyLocale.Irc($"Query.{nameof(EndOfWho)}");

	/// <summary>RPL_ENDOFWHOIS text.</summary>
	public static readonly string EndOfWhoIs = ReplyLocale.Irc($"Query.{nameof(EndOfWhoIs)}");

	/// <summary>RPL_ENDOFLIST text.</summary>
	public static readonly string EndOfList = ReplyLocale.Irc($"Query.{nameof(EndOfList)}");

	/// <summary>RPL_NOTOPIC text.</summary>
	public static readonly string NoTopicIsSet = ReplyLocale.Irc($"Query.{nameof(NoTopicIsSet)}");

	/// <summary>ERR_NOSUCHCHANNEL text.</summary>
	public static readonly string NoSuchChannel = ReplyLocale.Irc($"Query.{nameof(NoSuchChannel)}");

	/// <summary>ERR_NOSUCHNICK text.</summary>
	public static readonly string NoSuchNickChannel = ReplyLocale.Irc($"Query.{nameof(NoSuchNickChannel)}");

	/// <summary>The server label reported in RPL_WHOISSERVER and RPL_VERSION.</summary>
	public static readonly string IrcGateway = ReplyLocale.Irc($"Query.{nameof(IrcGateway)}");

	/// <summary>Trailing parameter of the RPL_WHOISIDLE line.</summary>
	public static readonly string SecondsIdleSignonTime = ReplyLocale.Irc($"Query.{nameof(SecondsIdleSignonTime)}");

	/// <summary>ERR_CHANOPRIVSNEEDED text when a topic change is refused.</summary>
	public static readonly string TopicManagedByServer = ReplyLocale.Irc($"Query.{nameof(TopicManagedByServer)}");

	/// <summary>ERR_CHANOPRIVSNEEDED text when a mode change is refused.</summary>
	public static readonly string ModesManagedByServer = ReplyLocale.Irc($"Query.{nameof(ModesManagedByServer)}");

	// ── MOTD ───────────────────────────────────────────────────────────────────────────────
	/// <summary>ERR_NOMOTD text.</summary>
	public static readonly string NoMotd = ReplyLocale.Irc($"Motd.{nameof(NoMotd)}");

	/// <summary>RPL_MOTDSTART text; <c>{0}</c> is the server name.</summary>
	public static readonly string MotdStart = ReplyLocale.Irc($"Motd.{nameof(MotdStart)}");

	/// <summary>RPL_ENDOFMOTD text.</summary>
	public static readonly string EndOfMotd = ReplyLocale.Irc($"Motd.{nameof(EndOfMotd)}");

	// ── LUSERS ─────────────────────────────────────────────────────────────────────────────
	/// <summary>RPL_LUSERCLIENT text; <c>{0}</c> is the account count.</summary>
	public static readonly string LusersClients = ReplyLocale.Irc($"Lusers.{nameof(LusersClients)}");

	/// <summary>RPL_LUSERCHANNELS trailing parameter.</summary>
	public static readonly string ChannelsFormed = ReplyLocale.Irc($"Lusers.{nameof(ChannelsFormed)}");

	/// <summary>RPL_LUSERME text; <c>{0}</c> is the client count.</summary>
	public static readonly string LusersMe = ReplyLocale.Irc($"Lusers.{nameof(LusersMe)}");

	// ── LIST / NAMES headers ───────────────────────────────────────────────────────────────
	/// <summary>RPL_LISTSTART column header for the channel column.</summary>
	public static readonly string ListChannel = ReplyLocale.Irc($"List.{nameof(ListChannel)}");

	/// <summary>RPL_LISTSTART column header for the user-count column.</summary>
	public static readonly string ListUsers = ReplyLocale.Irc($"List.{nameof(ListUsers)}");

	// ── authentication and command errors ──────────────────────────────────────────────────
	/// <summary>ERR_PASSWDMISMATCH text.</summary>
	public static readonly string PasswordIncorrect = ReplyLocale.Irc($"Errors.{nameof(PasswordIncorrect)}");

	/// <summary>ERR_NICKNAMEINUSE text.</summary>
	public static readonly string NicknameInUse = ReplyLocale.Irc($"Errors.{nameof(NicknameInUse)}");

	/// <summary>ERR_ERRONEUSNICKNAME text.</summary>
	public static readonly string ErroneousNickname = ReplyLocale.Irc($"Errors.{nameof(ErroneousNickname)}");

	/// <summary>ERR_NOTREGISTERED text.</summary>
	public static readonly string YouHaveNotRegistered = ReplyLocale.Irc($"Errors.{nameof(YouHaveNotRegistered)}");

	/// <summary>ERR_NEEDMOREPARAMS text.</summary>
	public static readonly string NotEnoughParameters = ReplyLocale.Irc($"Errors.{nameof(NotEnoughParameters)}");

	/// <summary>ERR_INVITEONLYCHAN text.</summary>
	public static readonly string CannotJoinChannel = ReplyLocale.Irc($"Errors.{nameof(CannotJoinChannel)}");

	/// <summary>ERR_ALREADYREGISTERED text.</summary>
	public static readonly string NicknameChangeNotSupported = ReplyLocale.Irc($"Errors.{nameof(NicknameChangeNotSupported)}");

	/// <summary>ERR_UNKNOWNCOMMAND text.</summary>
	public static readonly string UnknownCommand = ReplyLocale.Irc($"Errors.{nameof(UnknownCommand)}");

	/// <summary>ERR_CANNOTSENDTOCHAN text.</summary>
	public static readonly string CannotSendToChannel = ReplyLocale.Irc($"Errors.{nameof(CannotSendToChannel)}");
}