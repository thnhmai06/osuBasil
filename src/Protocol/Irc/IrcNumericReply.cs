namespace Basil.Protocol.Irc;

/// <summary>RFC 1459/2812 numeric reply codes, not the full table.</summary>
public enum IrcNumericReply : ushort
{
	/// <summary>Sent as the first reply after successful registration, welcoming the client.</summary>
	RplWelcome = 1,

	/// <summary>Names the server the client reached and the version it runs.</summary>
	RplYourHost = 2,

	/// <summary>Carries the time at which the server started.</summary>
	RplCreated = 3,

	/// <summary>Carries the server name, its version, and the mode letters it understands.</summary>
	RplMyInfo = 4,

	/// <summary>Carries the space-separated feature tokens the server supports.</summary>
	RplIsupport = 5,

	/// <summary>Carries the user modes the client has.</summary>
	RplUModeIs = 221,

	/// <summary>Carries the count of visible users and servers.</summary>
	RplLuserClient = 251,

	/// <summary>Carries the number of channels that currently exist.</summary>
	RplLuserChannels = 254,

	/// <summary>Carries the number of clients this server itself holds.</summary>
	RplLuserMe = 255,

	/// <summary>Carries the away message of a user who is marked away.</summary>
	RplAway = 301,

	/// <summary>Carries the subset of the queried nicknames that are currently online.</summary>
	RplIson = 303,

	/// <summary>Confirms that the client is no longer marked away.</summary>
	RplUnaway = 305,

	/// <summary>Confirms that the client is now marked away.</summary>
	RplNowAway = 306,

	/// <summary>Carries the hostmask replies of a USERHOST query.</summary>
	RplUserHost = 302,

	/// <summary>Carries a user's nick, hostmask, and real name.</summary>
	RplWhoIsUser = 311,

	/// <summary>Carries the server a user is connected to.</summary>
	RplWhoIsServer = 312,

	/// <summary>Marks the end of a WHO reply.</summary>
	RplEndOfWho = 315,

	/// <summary>Carries a user's idle time and sign-on time.</summary>
	RplWhoIsIdle = 317,

	/// <summary>Marks the end of a WHOIS reply.</summary>
	RplEndOfWhoIs = 318,

	/// <summary>Carries the channels a user has joined.</summary>
	RplWhoIsChannels = 319,

	/// <summary>Marks the start of a channel listing.</summary>
	RplListStart = 321,

	/// <summary>Carries one channel's name, member count, and topic in a channel listing.</summary>
	RplList = 322,

	/// <summary>Marks the end of a channel listing.</summary>
	RplListEnd = 323,

	/// <summary>Carries the modes currently set on a channel.</summary>
	RplChannelModeIs = 324,

	/// <summary>Indicates that a channel has no topic set.</summary>
	RplNoTopic = 331,

	/// <summary>Carries the account a user is logged in as.</summary>
	RplWhoIsAccount = 330,

	/// <summary>Carries the topic of a channel.</summary>
	RplTopic = 332,

	/// <summary>Carries the server's version.</summary>
	RplVersion = 351,

	/// <summary>Carries one entry of a WHO reply.</summary>
	RplWhoReply = 352,

	/// <summary>Carries a list of channel members.</summary>
	RplNamReply = 353,

	/// <summary>Marks the end of a channel member list.</summary>
	RplEndOfNames = 366,

	/// <summary>Carries one line of the message of the day.</summary>
	RplMotd = 372,

	/// <summary>Marks the start of the message of the day.</summary>
	RplMotdStart = 375,

	/// <summary>Marks the end of the message of the day.</summary>
	RplEndOfMotd = 376,

	/// <summary>Carries the server's local time.</summary>
	RplTime = 391,

	/// <summary>Indicates that the target nickname does not exist.</summary>
	ErrNoSuchNick = 401,

	/// <summary>Indicates that the target channel does not exist.</summary>
	ErrNoSuchChannel = 403,

	/// <summary>Indicates that the sender may not send to the target channel.</summary>
	ErrCannotSendToChannel = 404,

	/// <summary>Indicates that a message command named no recipient.</summary>
	ErrNoRecipient = 411,

	/// <summary>Indicates that a message command carried no text.</summary>
	ErrNoTextToSend = 412,

	/// <summary>Indicates that the line was longer than the server reads.</summary>
	ErrInputTooLong = 417,

	/// <summary>Indicates that the command is unknown or not implemented.</summary>
	ErrUnknownCommand = 421,

	/// <summary>Indicates that no message of the day is configured.</summary>
	ErrNoMotd = 422,

	/// <summary>Indicates that a nickname command carried no nickname.</summary>
	ErrNoNicknameGiven = 431,

	/// <summary>Indicates that the supplied nickname is malformed.</summary>
	ErrErroneousNickname = 432,

	/// <summary>Indicates that the requested nickname is already in use.</summary>
	ErrNicknameInUse = 433,

	/// <summary>Indicates that the client is not on the channel the command names.</summary>
	ErrNotOnChannel = 442,

	/// <summary>Indicates that the client must register before the command can be used.</summary>
	ErrNotRegistered = 451,

	/// <summary>Indicates that the command was sent without enough parameters.</summary>
	ErrNeedMoreParams = 461,

	/// <summary>Indicates that the command may not be used after registration has completed.</summary>
	ErrAlreadyRegistered = 462,

	/// <summary>Indicates that the supplied password does not match.</summary>
	ErrPasswdMismatch = 464,

	/// <summary>Indicates that the channel cannot be joined because it is invite-only.</summary>
	ErrInviteOnlyChan = 473,

	/// <summary>Indicates that the command requires channel operator privileges the sender lacks.</summary>
	ErrChanOPrivsNeeded = 482,

	/// <summary>Indicates that the client names a user mode the server does not have.</summary>
	ErrUModeUnknownFlag = 501,

	/// <summary>Indicates that the client may not change or view the modes of another user.</summary>
	ErrUsersDontMatch = 502,

	/// <summary>Reports that the client is logged in to an account (SASL).</summary>
	RplLoggedIn = 900,

	/// <summary>Reports that SASL authentication succeeded.</summary>
	RplSaslSuccess = 903,

	/// <summary>Reports that SASL authentication failed.</summary>
	ErrSaslFail = 904,

	/// <summary>Reports that the SASL exchange was aborted by the client.</summary>
	ErrSaslAborted = 906,

	/// <summary>Reports that the client already authenticated with SASL.</summary>
	ErrSaslAlready = 907,

	/// <summary>Lists the SASL mechanisms the server offers.</summary>
	RplSaslMechs = 908
}