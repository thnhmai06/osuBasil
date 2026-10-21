using Basil.Protocol.Irc;

namespace Basil.Protocol.Irc.Tests;

public class IrcMessageParserTests
{
	[Fact]
	public void Parse_CommandOnly_NoPrefixNoParams()
	{
		var message = IrcMessage.Parse("PING", null);

		Assert.Null(message.Prefix);
		Assert.Equal("PING", message.Command);
		Assert.Empty(message.Params);
	}

	[Fact]
	public void Parse_WithPrefixAndMiddleParams()
	{
		var message = IrcMessage.Parse(":alice!alice@basil JOIN #osu", null);

		Assert.Equal("alice!alice@basil", message.Prefix);
		Assert.Equal("JOIN", message.Command);
		Assert.Equal(["#osu"], message.Params);
	}

	[Fact]
	public void Parse_WithTrailingParam_KeepsSpacesInside()
	{
		var message = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there world", null);

		Assert.Equal("PRIVMSG", message.Command);
		Assert.Equal(["#osu", "hello there world"], message.Params);
	}

	[Fact]
	public void Parse_TrailingWithNoMiddleParams()
	{
		var message = IrcMessage.Parse("PASS :s3cret", null);

		Assert.Equal("PASS", message.Command);
		Assert.Equal(["s3cret"], message.Params);
	}

	[Fact]
	public void Parse_TrailingParamStartingWithColon_KeepsColon()
	{
		var message = IrcMessage.Parse("PRIVMSG #osu ::) hi", null);

		Assert.Equal(["#osu", ":) hi"], message.Params);
	}

	[Fact]
	public void Parse_EmptyLine_Fails()
	{
		Assert.False(IrcMessage.TryParse("", null, out _));
	}

	[Theory]
	[InlineData("USER guest 0 * :Real Name")]
	[InlineData(":irc.basil 001 alice :Welcome to the network")]
	[InlineData(":alice!alice@basil QUIT :Connection reset")]
	public void RoundTrip_ParseThenFormat_ProducesEquivalentLine(string line)
	{
		var message = IrcMessage.Parse(line, null);
		var formatted = message.ToString(null, null);

		Assert.Equal(line, formatted);
	}

	[Fact]
	public void ToString_WithoutArguments_ReturnsTheWireLine()
	{
		var message = IrcMessage.Parse(":alice!alice@basil PRIVMSG #osu :hello there", null);

		Assert.Equal(":alice!alice@basil PRIVMSG #osu :hello there", message.ToString());
	}
}