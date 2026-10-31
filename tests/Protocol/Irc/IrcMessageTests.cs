namespace Basil.Protocol.Irc.Tests;

public class IrcMessageTests
{
	[Fact]
	public void Parse_CommandOnly_NoPrefixNoParams()
	{
		var message = IrcMessage.Parse("PING", null);

		Assert.Null(message.Prefix);
		Assert.Equal("PING", message.Command);
		Assert.Empty(message.Parameters);
	}

	[Fact]
	public void Parse_WithoutPrefix_ReturnsNullPrefix()
	{
		var message = IrcMessage.Parse("PRIVMSG #osu :hello there", null);

		Assert.Null(message.Prefix);
		Assert.Equal("PRIVMSG", message.Command);
		Assert.Equal(["#osu", "hello there"], message.Parameters);
	}

	[Fact]
	public void Parse_WithPrefixAndMiddleParams()
	{
		var message = IrcMessage.Parse(":alice!alice@basil JOIN #osu", null);

		Assert.Equal("alice!alice@basil", message.Prefix);
		Assert.Equal("JOIN", message.Command);
		Assert.Equal(["#osu"], message.Parameters);
	}

	[Fact]
	public void Parse_MiddleParams_ReturnsEveryParam()
	{
		var message = IrcMessage.Parse("USER guest 0 *", null);

		Assert.Null(message.Prefix);
		Assert.Equal("USER", message.Command);
		Assert.Equal(["guest", "0", "*"], message.Parameters);
	}

	[Fact]
	public void Parse_WithTrailingParam_KeepsSpacesInside()
	{
		var message = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there world", null);

		Assert.Equal("PRIVMSG", message.Command);
		Assert.Equal(["#osu", "hello there world"], message.Parameters);
	}

	[Fact]
	public void Parse_TrailingWithNoMiddleParams()
	{
		var message = IrcMessage.Parse("PASS :s3cret", null);

		Assert.Equal("PASS", message.Command);
		Assert.Equal(["s3cret"], message.Parameters);
	}

	[Fact]
	public void Parse_TrailingParamStartingWithColon_KeepsColon()
	{
		var message = IrcMessage.Parse("PRIVMSG #osu ::) hi", null);

		Assert.Equal(["#osu", ":) hi"], message.Parameters);
	}

	[Fact]
	public void Parse_EmptyTrailing_ReturnsOneEmptyParam()
	{
		var message = IrcMessage.Parse("PING :", null);

		Assert.Equal([""], message.Parameters);
	}

	[Fact]
	public void Parse_ColonInsideMiddleParam_IsKept()
	{
		var message = IrcMessage.Parse("MODE #osu +b bad:word", null);

		Assert.Equal(["#osu", "+b", "bad:word"], message.Parameters);
	}

	[Fact]
	public void Parse_ConsecutiveSpacesBetweenTokens_CollapseToSeparators()
	{
		var message = IrcMessage.Parse("USER   guest    0  *", null);

		Assert.Equal("USER", message.Command);
		Assert.Equal(["guest", "0", "*"], message.Parameters);
	}

	[Theory]
	[InlineData(15)]
	[InlineData(16)]
	public void Parse_ManyParams_ReturnsEveryParam(int count)
	{
		var parameters = Enumerable.Range(1, count).Select(index => $"p{index}").ToArray();

		var message = IrcMessage.Parse($"CMD {string.Join(' ', parameters)}", null);

		Assert.Equal(parameters, message.Parameters);
	}

	[Fact]
	public void Parse_LowercaseCommand_IsUpperCased()
	{
		var message = IrcMessage.Parse("privmsg #osu :hi", null);

		Assert.Equal("PRIVMSG", message.Command);
	}

	[Fact]
	public void Parse_LeadingTagSection_IsSkipped()
	{
		var message = IrcMessage.Parse(
			"@time=2020-01-01T00:00:00Z :alice!alice@basil PRIVMSG #osu :hi", null);

		Assert.Equal("alice!alice@basil", message.Prefix);
		Assert.Equal("PRIVMSG", message.Command);
		Assert.Equal(["#osu", "hi"], message.Parameters);
	}

	[Fact]
	public void Parse_EmptyLine_Fails()
	{
		Assert.False(IrcMessage.TryParse("", null, out _));
	}

	[Fact]
	public void TryParse_Null_ReturnsFalse()
	{
		Assert.False(IrcMessage.TryParse(null, null, out var message));
		Assert.Null(message);
	}

	[Fact]
	public void TryParse_PrefixOnly_ReturnsFalse()
	{
		Assert.False(IrcMessage.TryParse(":p", null, out _));
	}

	[Fact]
	public void TryParse_EmptyPrefix_ReturnsFalse()
	{
		Assert.False(IrcMessage.TryParse(": PING", null, out _));
	}

	[Theory]
	[InlineData("@a=b")]
	[InlineData("@a=b ")]
	public void TryParse_TagSectionWithoutCommand_ReturnsFalse(string line)
	{
		Assert.False(IrcMessage.TryParse(line, null, out _));
	}

	[Fact]
	public void TryParse_OnlySpaces_ReturnsFalse()
	{
		Assert.False(IrcMessage.TryParse("   ", null, out _));
	}

	[Fact]
	public void TryParse_WellFormedLine_ReturnsTrueAndTheMessage()
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
	public void TryParse_LineWithControlCharacter_ReturnsFalse(string line)
	{
		Assert.False(IrcMessage.TryParse(line, null, out _));
	}

	[Theory]
	[InlineData("PING\r")]
	[InlineData("PING\n")]
	[InlineData("PI\0NG")]
	public void Parse_LineWithControlCharacter_ThrowsFormatException(string line)
	{
		Assert.Throws<FormatException>(() => IrcMessage.Parse(line, null));
	}

	[Theory]
	[InlineData("")]
	[InlineData(":p")]
	[InlineData(": PING")]
	[InlineData("   ")]
	public void Parse_MalformedLine_ThrowsFormatException(string line)
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
	public void RoundTrip_ParseThenFormat_ProducesEquivalentLine(string line)
	{
		var message = IrcMessage.Parse(line, null);
		var formatted = message.ToString();

		Assert.Equal(line, formatted);
	}

	[Fact]
	public void ToString_WithoutArguments_ReturnsTheWireLine()
	{
		var message = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there", null);

		Assert.Equal(":alice!alice@basil PRIVMSG #osu :hello there", message.ToString());
	}

	[Fact]
	public void ToString_WithoutPrefix_StartsWithTheCommand()
	{
		var message = new IrcMessage("JOIN", null, "#osu");

		Assert.Equal("JOIN #osu", message.ToString());
	}

	[Fact]
	public void ToString_WithPrefix_StartsWithThePrefixedSource()
	{
		var message = new IrcMessage("JOIN", "alice!alice@basil", "#osu");

		Assert.Equal(":alice!alice@basil JOIN #osu", message.ToString());
	}

	[Fact]
	public void ToString_LastParamWithSpace_IsPrefixedWithAColon()
	{
		var message = new IrcMessage("PRIVMSG", null, "#osu", "hello there");

		Assert.Equal("PRIVMSG #osu :hello there", message.ToString());
	}

	[Fact]
	public void ToString_LastParamStartingWithColon_IsPrefixedWithAColon()
	{
		var message = new IrcMessage("PRIVMSG", null, "#osu", ":) hi");

		Assert.Equal("PRIVMSG #osu ::) hi", message.ToString());
	}

	[Fact]
	public void ToString_EmptyLastParam_IsEmittedAsABareColon()
	{
		var message = new IrcMessage("PING", null, "");

		Assert.Equal("PING :", message.ToString());
	}

	[Fact]
	public void ToString_PlainLastParam_IsEmittedWithoutAColon()
	{
		var message = new IrcMessage("PING", null, "token");

		Assert.Equal("PING token", message.ToString());
	}

	[Fact]
	public void ToString_LastParamContainingColon_IsEmittedWithoutAColon()
	{
		var message = new IrcMessage("MODE", null, "#osu", "+b", "bad:word");

		Assert.Equal("MODE #osu +b bad:word", message.ToString());
	}

	[Theory]
	[InlineData("")]
	[InlineData("a b")]
	[InlineData(":a")]
	public void ToString_NonLastParamCannotBeMiddle_ThrowsArgumentException(string parameter)
	{
		var message = new IrcMessage("CMD", null, parameter, "last");

		Assert.Throws<ArgumentException>(() => message.ToString());
	}

	[Theory]
	[InlineData("PI\rNG", null, "p")]
	[InlineData("PING", "alice\r", "p")]
	[InlineData("PING", null, "p\n")]
	[InlineData("PING", "al\0ice", "p")]
	public void ToString_ControlCharacterInALinePart_ThrowsArgumentException(
		string command, string? prefix, string parameter)
	{
		var message = new IrcMessage(command, prefix, parameter);

		Assert.Throws<ArgumentException>(() => message.ToString());
	}

	[Fact]
	public void Equals_SameLineParsedTwice_ReturnsTrue()
	{
		var first = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there", null);
		var second = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there", null);

		Assert.Equal(first, second);
	}

	[Fact]
	public void GetHashCode_SameLineParsedTwice_ReturnsTheSameCode()
	{
		var first = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there", null);
		var second = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there", null);

		Assert.Equal(first.GetHashCode(), second.GetHashCode());
	}

	[Fact]
	public void Equals_DifferentCommand_ReturnsFalse()
	{
		var first = new IrcMessage("PRIVMSG", "alice!a@b", "#osu", "hi");
		var second = new IrcMessage("NOTICE", "alice!a@b", "#osu", "hi");

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void Equals_DifferentPrefix_ReturnsFalse()
	{
		var first = new IrcMessage("PRIVMSG", "alice!a@b", "#osu", "hi");
		var second = new IrcMessage("PRIVMSG", "carol!c@d", "#osu", "hi");

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void Equals_DifferentParamOrder_ReturnsFalse()
	{
		var first = new IrcMessage("CMD", null, "a", "b");
		var second = new IrcMessage("CMD", null, "b", "a");

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void Equals_DifferentParamCount_ReturnsFalse()
	{
		var first = new IrcMessage("CMD", null, "a", "b");
		var second = new IrcMessage("CMD", null, "a");

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void Equals_Null_ReturnsFalse()
	{
		var message = new IrcMessage("PING", null, "token");

		Assert.False(message.Equals(null));
	}

	[Theory]
	[InlineData(IrcNumericReply.RplWelcome, "001")]
	[InlineData(IrcNumericReply.RplIsupport, "005")]
	[InlineData(IrcNumericReply.ErrNicknameInUse, "433")]
	public void Numeric_Code_IsPaddedToThreeDigits(IrcNumericReply numericReply, string code)
	{
		var message = IrcMessage.Numeric("irc.basil", numericReply, "alice");

		Assert.Equal(code, message.Command);
	}

	[Fact]
	public void Numeric_WithArgs_EmitsTheTargetBeforeTheArgs()
	{
		var message = IrcMessage.Numeric(
			"irc.basil", IrcNumericReply.RplWelcome, "alice", "Welcome to the network");

		Assert.Equal(":irc.basil 001 alice :Welcome to the network", message.ToString());
	}

	[Fact]
	public void Numeric_WithoutArgs_EndsAtTheTarget()
	{
		var message = IrcMessage.Numeric("irc.basil", IrcNumericReply.RplNoTopic, "alice", "#osu");

		Assert.Equal(":irc.basil 331 alice #osu", message.ToString());
	}

	[Fact]
	public void Privmsg_FormatsTheWireLine()
	{
		var message = IrcMessage.Privmsg(new IrcUserPrefix("alice", "alice", "basil"), "#osu", "hello there");

		Assert.Equal(":alice!alice@basil PRIVMSG #osu :hello there", message.ToString());
	}

	[Fact]
	public void Part_WithoutReason_OmitsTheTrailingParam()
	{
		var message = IrcMessage.Part(new IrcUserPrefix("alice", "alice", "basil"), "#osu");

		Assert.Equal(":alice!alice@basil PART #osu", message.ToString());
	}

	[Fact]
	public void Part_WithReason_EmitsTheTrailingParam()
	{
		var message = IrcMessage.Part(new IrcUserPrefix("alice", "alice", "basil"), "#osu", "see you");

		Assert.Equal(":alice!alice@basil PART #osu :see you", message.ToString());
	}

	[Fact]
	public void Ping_FormatsTheWireLine()
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
	public void Pong_FormatsTheWireLine()
	{
		var message = IrcMessage.Pong("token");

		Assert.Equal("PONG token", message.ToString());
	}
}