namespace Basil.Protocol.Irc.Models;

public enum IrcNumericReply : ushort
{
	RplWelcome = 1,

	RplYourHost = 2,

	RplCreated = 3,

	RplMyInfo = 4,

	RplIsupport = 5,

	RplUModeIs = 221,

	RplLuserClient = 251,

	RplLuserChannels = 254,

	RplLuserMe = 255,

	RplAway = 301,

	RplIson = 303,

	RplUnaway = 305,

	RplNowAway = 306,

	RplUserHost = 302,

	RplWhoIsUser = 311,

	RplWhoIsServer = 312,

	RplEndOfWho = 315,

	RplWhoIsIdle = 317,

	RplEndOfWhoIs = 318,

	RplWhoIsChannels = 319,

	RplListStart = 321,

	RplList = 322,

	RplListEnd = 323,

	RplChannelModeIs = 324,

	RplNoTopic = 331,

	RplWhoIsAccount = 330,

	RplTopic = 332,

	RplVersion = 351,

	RplWhoReply = 352,

	RplNamReply = 353,

	RplEndOfNames = 366,

	RplEndOfBanList = 368,

	RplMotd = 372,

	RplMotdStart = 375,

	RplEndOfMotd = 376,

	RplTime = 391,

	ErrUnknownError = 400,

	ErrNoSuchNick = 401,

	ErrNoSuchChannel = 403,

	ErrCannotSendToChannel = 404,

	ErrNoRecipient = 411,

	ErrNoTextToSend = 412,

	ErrInputTooLong = 417,

	ErrUnknownCommand = 421,

	ErrNoMotd = 422,

	ErrNoNicknameGiven = 431,

	ErrErroneousNickname = 432,

	ErrNicknameInUse = 433,

	ErrNotOnChannel = 442,

	ErrNotRegistered = 451,

	ErrNeedMoreParams = 461,

	ErrAlreadyRegistered = 462,

	ErrPasswdMismatch = 464,

	ErrInviteOnlyChan = 473,

	ErrChanOPrivsNeeded = 482,

	ErrUModeUnknownFlag = 501,

	ErrUsersDontMatch = 502,

	RplLoggedIn = 900,

	RplSaslSuccess = 903,

	ErrSaslFail = 904,

	ErrSaslAborted = 906,

	ErrSaslAlready = 907,

	RplSaslMechs = 908
}