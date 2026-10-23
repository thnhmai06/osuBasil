namespace Basil.Protocol.Irc.Tests;

public class IrcNumericReplyTests
{
	[Theory]
	[InlineData(IrcNumericReply.RplWelcome, 1)]
	[InlineData(IrcNumericReply.RplYourHost, 2)]
	[InlineData(IrcNumericReply.RplCreated, 3)]
	[InlineData(IrcNumericReply.RplMyInfo, 4)]
	[InlineData(IrcNumericReply.RplIsupport, 5)]
	[InlineData(IrcNumericReply.RplNoTopic, 331)]
	[InlineData(IrcNumericReply.RplTopic, 332)]
	[InlineData(IrcNumericReply.RplNamReply, 353)]
	[InlineData(IrcNumericReply.RplEndOfNames, 366)]
	[InlineData(IrcNumericReply.RplMotd, 372)]
	[InlineData(IrcNumericReply.RplMotdStart, 375)]
	[InlineData(IrcNumericReply.RplEndOfMotd, 376)]
	[InlineData(IrcNumericReply.ErrNoSuchNick, 401)]
	[InlineData(IrcNumericReply.ErrNoSuchChannel, 403)]
	[InlineData(IrcNumericReply.ErrCannotSendToChannel, 404)]
	[InlineData(IrcNumericReply.ErrUnknownCommand, 421)]
	[InlineData(IrcNumericReply.ErrNicknameInUse, 433)]
	[InlineData(IrcNumericReply.ErrNeedMoreParams, 461)]
	[InlineData(IrcNumericReply.ErrAlreadyRegistered, 462)]
	[InlineData(IrcNumericReply.ErrPasswdMismatch, 464)]
	[InlineData(IrcNumericReply.ErrInviteOnlyChan, 473)]
	[InlineData(IrcNumericReply.ErrChanOPrivsNeeded, 482)]
	public void Value_MatchesRfcNumber(IrcNumericReply numericReply, int number)
	{
		Assert.Equal(number, (int)numericReply);
	}
}