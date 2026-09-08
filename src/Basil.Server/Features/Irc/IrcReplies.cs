using Basil.Server.Shared.Localization;

namespace Basil.Server.Features.Irc;

/// <summary>
///     The user-visible reply text the IRC gateway sends, the single source of truth for that
///     surface.
/// </summary>
/// <remarks>
///     Every literal an IRC client can read — the registration handshake, query replies, and
///     command-error messages — is a named member here. Production services read these members,
///     and tests assert against the same symbols, so the two cannot drift. The wording itself lives
///     outside the code, in this slice's <c>Locale/irc.en.json</c> fragment, so it can be edited
///     without a rebuild; each member's lookup key (via <see cref="LocaleCatalog.Get" />) is a
///     dotted key under <c>Irc.*</c>, so a member and its wording cannot silently fall out of sync.
///     Format strings use
///     <see cref="string.Format(string, object?[])" /> placeholders; fixed strings are plain text.
///     Deliberately changing the wording of a reply is a public-behavior change: an edit to the
///     locale file, and every test that pins it stays in sync.
/// </remarks>
public static class IrcReplies
{
	// ── registration handshake ──────────────────────────────────────────────────────────────
	/// <summary>RPL_WELCOME text; <c>{0}</c> is the server name, <c>{1}</c> the user's nick.</summary>
	public static readonly string Welcome = LocaleCatalog.Get($"Irc.Registration.{nameof(Welcome)}");

	/// <summary>RPL_YOURHOST text; <c>{0}</c> is the server name, <c>{1}</c> the version.</summary>
	public static readonly string YourHost = LocaleCatalog.Get($"Irc.Registration.{nameof(YourHost)}");

	/// <summary>RPL_CREATED text; <c>{0}</c> is the server creation timestamp.</summary>
	public static readonly string ServerCreated = LocaleCatalog.Get($"Irc.Registration.{nameof(ServerCreated)}");

	/// <summary>Trailing parameter of the RPL_ISUPPORT line.</summary>
	public static readonly string AreSupportedByThisServer =
		LocaleCatalog.Get($"Irc.Registration.{nameof(AreSupportedByThisServer)}");

	// ── query replies ──────────────────────────────────────────────────────────────────────
	/// <summary>RPL_ENDOFNAMES text.</summary>
	public static readonly string EndOfNames = LocaleCatalog.Get($"Irc.Query.{nameof(EndOfNames)}");

	/// <summary>RPL_ENDOFWHO text.</summary>
	public static readonly string EndOfWho = LocaleCatalog.Get($"Irc.Query.{nameof(EndOfWho)}");

	/// <summary>RPL_ENDOFWHOIS text.</summary>
	public static readonly string EndOfWhoIs = LocaleCatalog.Get($"Irc.Query.{nameof(EndOfWhoIs)}");

	/// <summary>RPL_ENDOFLIST text.</summary>
	public static readonly string EndOfList = LocaleCatalog.Get($"Irc.Query.{nameof(EndOfList)}");

	/// <summary>RPL_NOTOPIC text.</summary>
	public static readonly string NoTopicIsSet = LocaleCatalog.Get($"Irc.Query.{nameof(NoTopicIsSet)}");

	/// <summary>ERR_NOSUCHCHANNEL text.</summary>
	public static readonly string NoSuchChannel = LocaleCatalog.Get($"Irc.Query.{nameof(NoSuchChannel)}");

	/// <summary>ERR_NOSUCHNICK text.</summary>
	public static readonly string NoSuchNickChannel = LocaleCatalog.Get($"Irc.Query.{nameof(NoSuchNickChannel)}");

	/// <summary>The server label reported in RPL_WHOISSERVER and RPL_VERSION.</summary>
	public static readonly string IrcGateway = LocaleCatalog.Get($"Irc.Query.{nameof(IrcGateway)}");

	/// <summary>Trailing parameter of the RPL_WHOISIDLE line.</summary>
	public static readonly string SecondsIdleSignonTime =
		LocaleCatalog.Get($"Irc.Query.{nameof(SecondsIdleSignonTime)}");

	/// <summary>ERR_CHANOPRIVSNEEDED text when a topic change is refused.</summary>
	public static readonly string TopicManagedByServer = LocaleCatalog.Get($"Irc.Query.{nameof(TopicManagedByServer)}");

	/// <summary>ERR_CHANOPRIVSNEEDED text when a mode change is refused.</summary>
	public static readonly string ModesManagedByServer = LocaleCatalog.Get($"Irc.Query.{nameof(ModesManagedByServer)}");

	// ── MOTD ───────────────────────────────────────────────────────────────────────────────
	/// <summary>ERR_NOMOTD text.</summary>
	public static readonly string NoMotd = LocaleCatalog.Get($"Irc.Motd.{nameof(NoMotd)}");

	/// <summary>RPL_MOTDSTART text; <c>{0}</c> is the server name.</summary>
	public static readonly string MotdStart = LocaleCatalog.Get($"Irc.Motd.{nameof(MotdStart)}");

	/// <summary>RPL_ENDOFMOTD text.</summary>
	public static readonly string EndOfMotd = LocaleCatalog.Get($"Irc.Motd.{nameof(EndOfMotd)}");

	// ── LUSERS ─────────────────────────────────────────────────────────────────────────────
	/// <summary>RPL_LUSERCLIENT text; <c>{0}</c> is the account count.</summary>
	public static readonly string LusersClients = LocaleCatalog.Get($"Irc.Lusers.{nameof(LusersClients)}");

	/// <summary>RPL_LUSERCHANNELS trailing parameter.</summary>
	public static readonly string ChannelsFormed = LocaleCatalog.Get($"Irc.Lusers.{nameof(ChannelsFormed)}");

	/// <summary>RPL_LUSERME text; <c>{0}</c> is the client count.</summary>
	public static readonly string LusersMe = LocaleCatalog.Get($"Irc.Lusers.{nameof(LusersMe)}");

	// ── LIST / NAMES headers ───────────────────────────────────────────────────────────────
	/// <summary>RPL_LISTSTART column header for the channel column.</summary>
	public static readonly string ListChannel = LocaleCatalog.Get($"Irc.List.{nameof(ListChannel)}");

	/// <summary>RPL_LISTSTART column header for the user-count column.</summary>
	public static readonly string ListUsers = LocaleCatalog.Get($"Irc.List.{nameof(ListUsers)}");

	// ── authentication and command errors ──────────────────────────────────────────────────
	/// <summary>ERR_PASSWDMISMATCH text.</summary>
	public static readonly string PasswordIncorrect = LocaleCatalog.Get($"Irc.Errors.{nameof(PasswordIncorrect)}");

	/// <summary>ERR_NICKNAMEINUSE text.</summary>
	public static readonly string NicknameInUse = LocaleCatalog.Get($"Irc.Errors.{nameof(NicknameInUse)}");

	/// <summary>ERR_ERRONEUSNICKNAME text.</summary>
	public static readonly string ErroneousNickname = LocaleCatalog.Get($"Irc.Errors.{nameof(ErroneousNickname)}");

	/// <summary>ERR_NOTREGISTERED text.</summary>
	public static readonly string YouHaveNotRegistered =
		LocaleCatalog.Get($"Irc.Errors.{nameof(YouHaveNotRegistered)}");

	/// <summary>ERR_NEEDMOREPARAMS text.</summary>
	public static readonly string NotEnoughParameters = LocaleCatalog.Get($"Irc.Errors.{nameof(NotEnoughParameters)}");

	/// <summary>ERR_INVITEONLYCHAN text.</summary>
	public static readonly string CannotJoinChannel = LocaleCatalog.Get($"Irc.Errors.{nameof(CannotJoinChannel)}");

	/// <summary>ERR_ALREADYREGISTERED text.</summary>
	public static readonly string NicknameChangeNotSupported =
		LocaleCatalog.Get($"Irc.Errors.{nameof(NicknameChangeNotSupported)}");

	/// <summary>ERR_UNKNOWNCOMMAND text.</summary>
	public static readonly string UnknownCommand = LocaleCatalog.Get($"Irc.Errors.{nameof(UnknownCommand)}");

	/// <summary>ERR_CANNOTSENDTOCHAN text.</summary>
	public static readonly string CannotSendToChannel = LocaleCatalog.Get($"Irc.Errors.{nameof(CannotSendToChannel)}");
}