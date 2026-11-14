using Basil.Protocol.Irc.Models;

namespace Basil.Protocol.Irc.Tests;

public class IrcMessageTests
{
	[Fact]
	public void CommandOnlyLine_Parsed()
	{
		var message = IrcMessage.Parse("PING", null);

		Assert.Null(message.Prefix);
		Assert.Equal("PING", message.Command);
		Assert.Empty(message.Parameters);
	}

	[Fact]
	public void NoPrefix_NullPrefix()
	{
		var message = IrcMessage.Parse("PRIVMSG #osu :hello there", null);

		Assert.Null(message.Prefix);
		Assert.Equal("PRIVMSG", message.Command);
		Assert.Equal(["#osu", "hello there"], message.Parameters);
	}

	[Fact]
	public void PrefixAndParams_Parsed()
	{
		var message = IrcMessage.Parse(":alice!alice@basil JOIN #osu", null);

		Assert.Equal("alice!alice@basil", message.Prefix);
		Assert.Equal("JOIN", message.Command);
		Assert.Equal(["#osu"], message.Parameters);
	}

	[Fact]
	public void SeveralMiddleParams_AllReturned()
	{
		var message = IrcMessage.Parse("USER guest 0 *", null);

		Assert.Null(message.Prefix);
		Assert.Equal("USER", message.Command);
		Assert.Equal(["guest", "0", "*"], message.Parameters);
	}

	[Fact]
	public void TrailingParam_KeepsSpaces()
	{
		var message = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there world", null);

		Assert.Equal("PRIVMSG", message.Command);
		Assert.Equal(["#osu", "hello there world"], message.Parameters);
	}

	[Fact]
	public void TrailingOnly_Parsed()
	{
		var message = IrcMessage.Parse("PASS :s3cret", null);

		Assert.Equal("PASS", message.Command);
		Assert.Equal(["s3cret"], message.Parameters);
	}

	[Fact]
	public void TrailingColon_Kept()
	{
		var message = IrcMessage.Parse("PRIVMSG #osu ::) hi", null);

		Assert.Equal(["#osu", ":) hi"], message.Parameters);
	}

	[Fact]
	public void EmptyTrailing_OneEmptyParam()
	{
		var message = IrcMessage.Parse("PING :", null);

		Assert.Equal([""], message.Parameters);
	}

	[Fact]
	public void ColonInMiddle_Kept()
	{
		var message = IrcMessage.Parse("MODE #osu +b bad:word", null);

		Assert.Equal(["#osu", "+b", "bad:word"], message.Parameters);
	}

	[Fact]
	public void ConsecutiveSpaces_Collapse()
	{
		var message = IrcMessage.Parse("USER   guest    0  *", null);

		Assert.Equal("USER", message.Command);
		Assert.Equal(["guest", "0", "*"], message.Parameters);
	}

	[Theory]
	[InlineData(15)]
	[InlineData(16)]
	public void ManyParams_AllReturned(int count)
	{
		var parameters = Enumerable.Range(1, count).Select(index => $"p{index}").ToArray();

		var message = IrcMessage.Parse($"CMD {string.Join(' ', parameters)}", null);

		Assert.Equal(parameters, message.Parameters);
	}

	[Fact]
	public void LowercaseCommand_UpperCased()
	{
		var message = IrcMessage.Parse("privmsg #osu :hi", null);

		Assert.Equal("PRIVMSG", message.Command);
	}

	[Fact]
	public void LeadingTags_Skipped()
	{
		var message = IrcMessage.Parse(
			"@time=2020-01-01T00:00:00Z :alice!alice@basil PRIVMSG #osu :hi", null);

		Assert.Equal("alice!alice@basil", message.Prefix);
		Assert.Equal("PRIVMSG", message.Command);
		Assert.Equal(["#osu", "hi"], message.Parameters);
	}

	[Fact]
	public void EmptyLine_Fails()
	{
		Assert.False(IrcMessage.TryParse("", null, out _));
	}

	[Fact]
	public void NullLine_False()
	{
		Assert.False(IrcMessage.TryParse(null, null, out var message));
		Assert.Null(message);
	}

	[Fact]
	public void PrefixOnly_False()
	{
		Assert.False(IrcMessage.TryParse(":p", null, out _));
	}

	[Fact]
	public void EmptyPrefix_False()
	{
		Assert.False(IrcMessage.TryParse(": PING", null, out _));
	}

	[Theory]
	[InlineData("@a=b")]
	[InlineData("@a=b ")]
	public void TagsWithoutCommand_False(string line)
	{
		Assert.False(IrcMessage.TryParse(line, null, out _));
	}

	[Fact]
	public void SpacesOnly_False()
	{
		Assert.False(IrcMessage.TryParse("   ", null, out _));
	}

	[Fact]
	public void WellFormedLine_TrueWithMessage()
	{
		Assert.True(IrcMessage.TryParse(":irc.basil 001 alice :Welcome to the network", null, out var message));
		Assert.NotNull(message);

		Assert.Equal("001", message.Command);
		Assert.Equal(["alice", "Welcome to the network"], message.Parameters);
	}

	[Theory]
	[InlineData("PING\r")]
	[InlineData("PING\n")]
	[InlineData("PING\r\n")]
	[InlineData("PI\0NG")]
	public void ControlCharacter_False(string line)
	{
		Assert.False(IrcMessage.TryParse(line, null, out _));
	}

	[Theory]
	[InlineData("PING\r")]
	[InlineData("PING\n")]
	[InlineData("PI\0NG")]
	public void ControlCharacter_FormatException(string line)
	{
		Assert.Throws<FormatException>(() => IrcMessage.Parse(line, null));
	}

	[Theory]
	[InlineData("")]
	[InlineData(":p")]
	[InlineData(": PING")]
	[InlineData("   ")]
	public void MalformedLine_FormatException(string line)
	{
		Assert.Throws<FormatException>(() => IrcMessage.Parse(line, null));
	}

	[Theory]
	[InlineData("PING")]
	[InlineData("USER guest 0 * :Real Name")]
	[InlineData(":irc.basil 001 alice :Welcome to the network")]
	[InlineData(":alice!alice@basil QUIT :Connection reset")]
	[InlineData(":alice!alice@basil PART #osu :bye now")]
	[InlineData("PRIVMSG #osu ::) hi")]
	[InlineData("CMD :")]
	[InlineData("MODE #osu +m")]
	public void ParseThenFormat_SameLine(string line)
	{
		var message = IrcMessage.Parse(line, null);
		var formatted = message.ToString();

		Assert.Equal(line, formatted);
	}

	[Fact]
	public void NoArguments_WireLine()
	{
		var message = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there", null);

		Assert.Equal(":alice!alice@basil PRIVMSG #osu :hello there", message.ToString());
	}

	[Fact]
	public void NoPrefix_StartsWithCommand()
	{
		var message = new IrcMessage("JOIN", null, "#osu");

		Assert.Equal("JOIN #osu", message.ToString());
	}

	[Fact]
	public void WithPrefix_StartsWithSource()
	{
		var message = new IrcMessage("JOIN", "alice!alice@basil", "#osu");

		Assert.Equal(":alice!alice@basil JOIN #osu", message.ToString());
	}

	[Fact]
	public void LastParamSpace_ColonPrefix()
	{
		var message = new IrcMessage("PRIVMSG", null, "#osu", "hello there");

		Assert.Equal("PRIVMSG #osu :hello there", message.ToString());
	}

	[Fact]
	public void ColonStartParam_ColonPrefixed()
	{
		var message = new IrcMessage("PRIVMSG", null, "#osu", ":) hi");

		Assert.Equal("PRIVMSG #osu ::) hi", message.ToString());
	}

	[Fact]
	public void EmptyLastParam_BareColon()
	{
		var message = new IrcMessage("PING", null, "");

		Assert.Equal("PING :", message.ToString());
	}

	[Fact]
	public void PlainLastParam_NoColon()
	{
		var message = new IrcMessage("PING", null, "token");

		Assert.Equal("PING token", message.ToString());
	}

	[Fact]
	public void InnerColonParam_NoColon()
	{
		var message = new IrcMessage("MODE", null, "#osu", "+b", "bad:word");

		Assert.Equal("MODE #osu +b bad:word", message.ToString());
	}

	[Theory]
	[InlineData("")]
	[InlineData("a b")]
	[InlineData(":a")]
	public void ParamBeforeLast_ArgumentException(string parameter)
	{
		var message = new IrcMessage("CMD", null, parameter, "last");

		Assert.Throws<ArgumentException>(() => message.ToString());
	}

	[Theory]
	[InlineData("PI\rNG", null, "p")]
	[InlineData("PING", "alice\r", "p")]
	[InlineData("PING", null, "p\n")]
	[InlineData("PING", "al\0ice", "p")]
	public void ControlCharacter_ArgumentException(
		string command, string? prefix, string parameter)
	{
		var message = new IrcMessage(command, prefix, parameter);

		Assert.Throws<ArgumentException>(() => message.ToString());
	}

	[Fact]
	public void SameLineTwice_Equal()
	{
		var first = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there", null);
		var second = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there", null);

		Assert.Equal(first, second);
	}

	[Fact]
	public void SameLineTwice_SameHash()
	{
		var first = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there", null);
		var second = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there", null);

		Assert.Equal(first.GetHashCode(), second.GetHashCode());
	}

	[Fact]
	public void DifferentCommand_Unequal()
	{
		var first = new IrcMessage("PRIVMSG", "alice!a@b", "#osu", "hi");
		var second = new IrcMessage("NOTICE", "alice!a@b", "#osu", "hi");

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void DifferentPrefix_Unequal()
	{
		var first = new IrcMessage("PRIVMSG", "alice!a@b", "#osu", "hi");
		var second = new IrcMessage("PRIVMSG", "carol!c@d", "#osu", "hi");

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void DifferentParamOrder_Unequal()
	{
		var first = new IrcMessage("CMD", null, "a", "b");
		var second = new IrcMessage("CMD", null, "b", "a");

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void DifferentParamCount_Unequal()
	{
		var first = new IrcMessage("CMD", null, "a", "b");
		var second = new IrcMessage("CMD", null, "a");

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void NullOther_Unequal()
	{
		var message = new IrcMessage("PING", null, "token");

		Assert.False(message.Equals(null));
	}

	[Theory]
	[InlineData(IrcNumericReply.RplWelcome, "001")]
	[InlineData(IrcNumericReply.RplIsupport, "005")]
	[InlineData(IrcNumericReply.ErrNicknameInUse, "433")]
	public void Code_ThreeDigitPadding(IrcNumericReply numericReply, string code)
	{
		var message = IrcMessage.Numeric("irc.basil", numericReply, "alice");

		Assert.Equal(code, message.Command);
	}

	[Fact]
	public void WithArgs_TargetBeforeParams()
	{
		var message = IrcMessage.Numeric(
			"irc.basil", IrcNumericReply.RplWelcome, "alice", "Welcome to the network");

		Assert.Equal(":irc.basil 001 alice :Welcome to the network", message.ToString());
	}

	[Fact]
	public void WithoutArgs_EndsAtTarget()
	{
		var message = IrcMessage.Numeric("irc.basil", IrcNumericReply.RplNoTopic, "alice", "#osu");

		Assert.Equal(":irc.basil 331 alice #osu", message.ToString());
	}

	[Fact]
	public void Privmsg_WireLine()
	{
		var message = IrcMessage.Privmsg(new IrcUserPrefix("alice", "alice", "basil"), "#osu", "hello there");

		Assert.Equal(":alice!alice@basil PRIVMSG #osu :hello there", message.ToString());
	}

	[Fact]
	public void PartWithoutReason_NoTrailingParam()
	{
		var message = IrcMessage.Part(new IrcUserPrefix("alice", "alice", "basil"), "#osu");

		Assert.Equal(":alice!alice@basil PART #osu", message.ToString());
	}

	[Fact]
	public void PartWithReason_TrailingParam()
	{
		var message = IrcMessage.Part(new IrcUserPrefix("alice", "alice", "basil"), "#osu", "see you");

		Assert.Equal(":alice!alice@basil PART #osu :see you", message.ToString());
	}

	[Fact]
	public void Ping_WireLine()
	{
		var message = IrcMessage.Ping("token");

		Assert.Equal("PING token", message.ToString());
	}

	[Fact]
	public void Ping_HasNoPrefix()
	{
		var message = IrcMessage.Ping("token");

		Assert.Null(message.Prefix);
	}

	[Fact]
	public void Pong_WireLine()
	{
		var message = IrcMessage.Pong("token");

		Assert.Equal("PONG token", message.ToString());
	}
}