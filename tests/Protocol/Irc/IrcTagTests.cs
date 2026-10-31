namespace Basil.Protocol.Irc.Tests;

/// <summary>Message tags, with the expected text taken from the IRCv3 message-tags specification.</summary>
public class IrcTagTests
{
	[Fact]
	public void Parse_TagsAndPrefix_ReadsEveryTagInOrder()
	{
		var message = IrcMessage.Parse("@id=123AB;rose;+draft/x=y :nick!u@h PRIVMSG #chan :hi", null);

		Assert.Equal([new IrcTag("id", "123AB"), new IrcTag("rose"), new IrcTag("+draft/x", "y")], message.Tags);
		Assert.Equal("nick!u@h", message.Prefix);
		Assert.Equal(["#chan", "hi"], message.Parameters);
	}

	[Fact]
	public void Parse_EscapedValue_IsUnescaped()
	{
		// The specification's table: \: is ";", \s is " ", \\ is "\", \r is CR, \n is LF.
		var message = IrcMessage.Parse(@"@a=1\:2\s3\\4\r\n5 PING x", null);

		Assert.Equal("1;2 3\\4\r\n5", message.Tag("a"));
	}

	[Theory]
	[InlineData(@"@a=x\ PING", "x")]
	[InlineData(@"@a=x\b PING", "xb")]
	public void Parse_InvalidEscape_DropsTheBackslash(string line, string expected)
	{
		Assert.Equal(expected, IrcMessage.Parse(line, null).Tag("a"));
	}

	[Fact]
	public void Parse_EmptyValue_IsTheSameAsNoValue()
	{
		var message = IrcMessage.Parse("@a=;b PING", null);

		Assert.Equal([new IrcTag("a"), new IrcTag("b")], message.Tags);
		Assert.Equal(string.Empty, message.Tag("a"));
		Assert.Null(message.Tag("missing"));
	}

	[Fact]
	public void ToString_Tags_AreEscapedAndPrecedeThePrefix()
	{
		var message = new IrcMessage("PRIVMSG", "n!u@h", "#c", "hi")
			.WithTag("time", "2026-01-01T00:00:00.000Z").WithTag("note", "a b;c\\d").WithTag("flag");

		Assert.Equal(@"@time=2026-01-01T00:00:00.000Z;note=a\sb\:c\\d;flag :n!u@h PRIVMSG #c hi", message.ToString());
	}

	[Fact]
	public void ToString_NoTags_HasNoAtSign()
	{
		Assert.Equal("PING x", new IrcMessage("PING", null, "x").ToString());
	}

	[Fact]
	public void RoundTrip_TaggedLine_IsStable()
	{
		const string line = @"@a=b\sc;d :p PRIVMSG #x :t t";

		Assert.Equal(line, IrcMessage.Parse(line, null).ToString());
	}

	[Fact]
	public void Equals_ComparesTags()
	{
		var plain = new IrcMessage("PING", null, "x");

		Assert.NotEqual(plain, plain.WithTag("a"));
		Assert.Equal(plain.WithTag("a", "1"), plain.WithTag("a", "1"));
	}

	[Fact]
	public void ToString_ForceTrailing_WritesAColonEvenWithoutASpace()
	{
		var message = new IrcMessage("PRIVMSG", "n!u@h", "#c", "hi") { ForceTrailing = true };

		Assert.Equal(":n!u@h PRIVMSG #c :hi", message.ToString());
		Assert.Equal(":n!u@h PRIVMSG #c hi", new IrcMessage("PRIVMSG", "n!u@h", "#c", "hi").ToString());
	}
}