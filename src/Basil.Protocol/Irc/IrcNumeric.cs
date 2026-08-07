namespace Basil.Protocol.Irc;

/// <summary>RFC 1459/2812 numeric replies actually used by Basil's IRC bridge, not the full table.</summary>
public enum IrcNumeric : ushort
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

	/// <summary>Indicates that the sender may not send the target channel.</summary>
	ErrCannotSendToChannel = 404,

	/// <summary>Indicates that the command is unknown or not implemented.</summary>
	ErrUnknownCommand = 421,

	/// <summary>Indicates that no message of the day is configured.</summary>
	ErrNoMotd = 422,

	/// <summary>Indicates that the supplied nickname is malformed.</summary>
	ErrErroneousNickname = 432,

	/// <summary>Indicates that the requested nickname is already in use.</summary>
	ErrNicknameInUse = 433,

	/// <summary>Indicates that the client must register before the command can be used.</summary>
	ErrNotRegistered = 451,

	/// <summary>Indicates that the command was sent without enough parameters.</summary>
	ErrNeedMoreParams = 461,

	/// <summary>Indicates that the command may not be used after registration has completed.</summary>
	ErrAlreadyRegistered = 462,

	/// <summary>Indicates that the supplied password does not match.</summary>
	ErrPasswdMismatch = 464,

	/// <summary>Indicates that the channel exists but the sender has no standing to join it.</summary>
	ErrInviteOnlyChan = 473,

	/// <summary>Indicates that the command requires channel operator privileges the sender lacks.</summary>
	ErrChanOPrivsNeeded = 482
}
