namespace Basil.Protocol.Irc;

/// <summary>RFC 1459/2812 numeric replies actually used by Basil's IRC bridge, not the full table.</summary>
public enum IrcNumeric
{
	/// <summary>Sent as the first reply after successful registration, welcoming the client.</summary>
	RplWelcome = 1,

	/// <summary>Carries the topic of a channel.</summary>
	RplTopic = 332,

	/// <summary>Carries a list of channel members.</summary>
	RplNamReply = 353,

	/// <summary>Marks the end of a channel member list.</summary>
	RplEndOfNames = 366,

	/// <summary>Indicates that the target nickname does not exist.</summary>
	ErrNoSuchNick = 401,

	/// <summary>Indicates that the target channel does not exist.</summary>
	ErrNoSuchChannel = 403,

	/// <summary>Indicates that the command is unknown or not implemented.</summary>
	ErrUnknownCommand = 421,

	/// <summary>Indicates that the requested nickname is already in use.</summary>
	ErrNicknameInUse = 433,

	/// <summary>Indicates that the client must register before the command can be used.</summary>
	ErrNotRegistered = 451,

	/// <summary>Indicates that the supplied password does not match.</summary>
	ErrPasswdMismatch = 464
}