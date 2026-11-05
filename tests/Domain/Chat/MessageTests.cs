using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Chat;

/// <summary>Verifies which texts can be the content of a chat message.</summary>
public class MessageTests
{
	private static readonly UserRecord Author = new() { Id = 1, Value = new UserData { Name = "author" } };

	private static Message Make(string content)
	{
		return new Message { Author = Author, Content = content, Timestamp = DateTimeOffset.UnixEpoch };
	}

	[Theory]
	[InlineData("hello")]
	[InlineData("a")]
	[InlineData("!mp make room")]
	[InlineData("xin chào, 你好, こんにちは 🎵")]
	[InlineData("tab\there")]
	[InlineData("line\nbreak\r\n")]
	[InlineData("\u0001ACTION waves\u0001")]
	public void Content_NormalText_IsAccepted(string content)
	{
		Assert.Equal(content, Make(content).Content);
	}

	[Theory]
	[InlineData(" ")]
	[InlineData("   ")]
	[InlineData("\t")]
	[InlineData(" ")]
	[InlineData("　")]
	public void Content_OnlyWhitespace_IsAccepted(string content)
	{
		Assert.Equal(content, Make(content).Content);
	}

	[Fact]
	public void Content_Empty_Throws()
	{
		Assert.Throws<ArgumentException>(() => Make(""));
	}

	[Fact]
	public void Content_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => Make(null!));
	}

	[Theory]
	[InlineData("\0")]
	[InlineData("a\0b")]
	[InlineData("abc\0")]
	[InlineData("\u0002")]
	[InlineData("\u0007bell")]
	[InlineData("esc\u001b[0m")]
	[InlineData("\u007f")]
	[InlineData("\u0085")]
	[InlineData("\u009f")]
	public void Content_ControlCharacter_Throws(string content)
	{
		Assert.Throws<ArgumentException>(() => Make(content));
	}

	[Fact]
	public void Content_LongText_IsAccepted()
	{
		var content = new string('x', 100_000);

		Assert.Equal(content, Make(content).Content);
	}

	[Fact]
	public void Content_ChangedWithAnInvalidValue_Throws()
	{
		var message = Make("hello");

		Assert.Throws<ArgumentException>(() => message with { Content = "" });
	}

	[Fact]
	public void IsNotice_NotGiven_IsFalse()
	{
		Assert.False(Make("hello").IsNotice);
	}

	[Theory]
	[InlineData("hello", true)]
	[InlineData(" ", true)]
	[InlineData("", false)]
	[InlineData("a\0", false)]
	public void IsValidContent_Text_AgreesWithContent(string text, bool expected)
	{
		Assert.Equal(expected, Message.IsValidContent(text));
	}
}