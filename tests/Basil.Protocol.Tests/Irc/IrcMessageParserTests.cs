using Basil.Protocol.Irc;

namespace Basil.Protocol.Tests.Irc;

public class IrcMessageParserTests
{
    [Fact]
    public void Parse_CommandOnly_NoPrefixNoParams()
    {
        var message = IrcMessageParser.Parse("PING");

        Assert.Null(message.Prefix);
        Assert.Equal("PING", message.Command);
        Assert.Empty(message.Params);
    }

    [Fact]
    public void Parse_WithPrefixAndMiddleParams()
    {
        var message = IrcMessageParser.Parse(":alice!alice@basil JOIN #osu");

        Assert.Equal("alice!alice@basil", message.Prefix);
        Assert.Equal("JOIN", message.Command);
        Assert.Equal(["#osu"], message.Params);
    }

    [Fact]
    public void Parse_WithTrailingParam_KeepsSpacesInside()
    {
        var message = IrcMessageParser.Parse(":alice!alice@basil PRIVMSG #osu :hello there world");

        Assert.Equal("PRIVMSG", message.Command);
        Assert.Equal(["#osu", "hello there world"], message.Params);
    }

    [Fact]
    public void Parse_TrailingWithNoMiddleParams()
    {
        var message = IrcMessageParser.Parse("PASS :s3cret");

        Assert.Equal("PASS", message.Command);
        Assert.Equal(["s3cret"], message.Params);
    }

    [Fact]
    public void Parse_TrailingParamStartingWithColon_KeepsColon()
    {
        var message = IrcMessageParser.Parse("PRIVMSG #osu ::) hi");

        Assert.Equal(["#osu", ":) hi"], message.Params);
    }

    [Fact]
    public void Parse_EmptyLine_Fails()
    {
        Assert.False(IrcMessageParser.TryParse("", out _));
    }

    [Theory]
    [InlineData("USER guest 0 * :Real Name")]
    [InlineData(":irc.basil 001 alice :Welcome to the network")]
    [InlineData(":alice!alice@basil QUIT :Connection reset")]
    public void RoundTrip_ParseThenFormat_ProducesEquivalentLine(string line)
    {
        var message = IrcMessageParser.Parse(line);
        var formatted = IrcMessageWriter.Format(message);

        Assert.Equal(line, formatted);
    }
}
