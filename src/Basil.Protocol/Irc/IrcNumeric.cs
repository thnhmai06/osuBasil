namespace Basil.Protocol.Irc;

/// <summary>RFC 1459/2812 numeric replies actually used by Basil's IRC bridge — not the full table.</summary>
public enum IrcNumeric
{
    RplWelcome = 1,
    RplTopic = 332,
    RplNamReply = 353,
    RplEndOfNames = 366,
    ErrNoSuchNick = 401,
    ErrNoSuchChannel = 403,
    ErrUnknownCommand = 421,
    ErrNicknameInUse = 433,
    ErrNotRegistered = 451,
    ErrPasswdMismatch = 464
}
