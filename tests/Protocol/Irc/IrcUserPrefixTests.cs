namespace Basil.Protocol.Irc.Tests;

public class IrcUserPrefixTests
{
	[Fact]
	public void TryParse_NickOnly_ReturnsTheNickWithoutUserOrHost()
	{
		Assert.True(IrcUserPrefix.TryParse("alice", null, out var prefix));

		Assert.Equal("alice", prefix.Nick);
		Assert.Null(prefix.User);
		Assert.Null(prefix.Host);
	}

	[Fact]
	public void TryParse_NickAndHost_ReturnsTheNickAndHostWithoutUser()
	{
		Assert.True(IrcUserPrefix.TryParse("alice@basil", null, out var prefix));

		Assert.Equal("alice", prefix.Nick);
		Assert.Null(prefix.User);
		Assert.Equal("basil", prefix.Host);
	}

	[Fact]
	public void TryParse_NickUserHost_ReturnsEveryPart()
	{
		Assert.True(IrcUserPrefix.TryParse("alice!alice@basil", null, out var prefix));

		Assert.Equal("alice", prefix.Nick);
		Assert.Equal("alice", prefix.User);
		Assert.Equal("basil", prefix.Host);
	}

	[Theory]
	[InlineData("")]
	[InlineData("@basil")]
	[InlineData("alice@")]
	[InlineData("alice!@basil")]
	[InlineData("!alice@basil")]
	[InlineData("alice!alice")]
	[InlineData("alice!alice@")]
	public void TryParse_MalformedPrefix_ReturnsFalse(string text)
	{
		Assert.False(IrcUserPrefix.TryParse(text, null, out _));
	}

	[Fact]
	public void TryParse_Null_ReturnsFalse()
	{
		Assert.False(IrcUserPrefix.TryParse(null, null, out _));
	}

	[Fact]
	public void TryParse_MalformedPrefix_ReturnsTheDefaultPrefix()
	{
		Assert.False(IrcUserPrefix.TryParse("@basil", null, out var prefix));

		Assert.Equal(default, prefix);
	}

	[Fact]
	public void Parse_MalformedPrefix_ThrowsFormatException()
	{
		Assert.Throws<FormatException>(() => IrcUserPrefix.Parse("alice!alice", null));
	}

	[Fact]
	public void Constructor_UserWithoutHost_ThrowsArgumentException()
	{
		Assert.Throws<ArgumentException>(() => new IrcUserPrefix("alice", "alice"));
	}

	[Fact]
	public void Default_HasNoParts()
	{
		var prefix = default(IrcUserPrefix);

		Assert.Null(prefix.Nick);
		Assert.Null(prefix.User);
		Assert.Null(prefix.Host);
	}

	[Theory]
	[InlineData("alice")]
	[InlineData("alice@basil")]
	[InlineData("alice!alice@basil")]
	public void Parse_ThenToString_ReturnsTheSameText(string text)
	{
		Assert.Equal(text, IrcUserPrefix.Parse(text, null).ToString());
	}

	[Fact]
	public void ToString_NickOnly_ReturnsTheNick()
	{
		var prefix = new IrcUserPrefix("alice");

		Assert.Equal("alice", prefix.ToString());
	}

	[Fact]
	public void ToString_NickAndHost_ReturnsNickAtHost()
	{
		var prefix = new IrcUserPrefix("alice", null, "basil");

		Assert.Equal("alice@basil", prefix.ToString());
	}

	[Fact]
	public void ToString_NickUserAndHost_ReturnsNickUserHost()
	{
		var prefix = new IrcUserPrefix("alice", "alice", "basil");

		Assert.Equal("alice!alice@basil", prefix.ToString());
	}
}