using Basil.Infrastructure.Bot.Handlers.Commands;

namespace Basil.Infrastructure.Bot.Tests.Commands;

/// <summary>
///     The syntax of a command message: segments chained by <c>;</c> and <c>&amp;&amp;</c>, words split on spaces, a
///     double quote that groups words, and a backslash that makes a following quote or backslash an ordinary character.
/// </summary>
public sealed class CommandLineTests
{
	[Fact]
	public void NoOperators_OneSegment()
	{
		var segments = CommandLine.Split("!mp settings");

		var segment = Assert.Single(segments);
		Assert.Equal("!mp settings", segment.Text);
		Assert.Equal(ChainOperator.Semicolon, segment.Operator);
	}

	[Fact]
	public void Semicolon_AlwaysRunsSegment()
	{
		var segments = CommandLine.Split("!mp lock;!mp unlock");

		Assert.Equal(["!mp lock", "!mp unlock"], segments.Select(segment => segment.Text));
		Assert.Equal(ChainOperator.Semicolon, segments[1].Operator);
	}

	[Fact]
	public void DoubleAmpersand_RunsAfterSuccess()
	{
		var segments = CommandLine.Split("!mp lock && !mp unlock");

		Assert.Equal(["!mp lock", "!mp unlock"], segments.Select(segment => segment.Text));
		Assert.Equal(ChainOperator.And, segments[1].Operator);
	}

	[Fact]
	public void SingleAmpersand_NotOperator()
	{
		var segment = Assert.Single(CommandLine.Split("!mp name A & B"));

		Assert.Equal("!mp name A & B", segment.Text);
	}

	[Fact]
	public void EmptySegments_AreLeftOut()
	{
		var segments = CommandLine.Split("!mp lock;; ;!mp unlock");

		Assert.Equal(["!mp lock", "!mp unlock"], segments.Select(segment => segment.Text));
	}

	[Fact]
	public void OperatorInQuotes_NoSplit()
	{
		var segment = Assert.Single(CommandLine.Split("!mp name \"a; b && c\""));

		Assert.Equal("!mp name \"a; b && c\"", segment.Text);
	}

	[Fact]
	public void EscapedInQuotes_QuotesNotEnded()
	{
		var segment = Assert.Single(CommandLine.Split("!mp name \"a\\\";b\""));

		Assert.Equal("!mp name \"a\\\";b\"", segment.Text);
	}

	[Fact]
	public void EscapedQuoteOutside_NoQuoteStart()
	{
		var segments = CommandLine.Split("!mp name \\\"a;!mp close");

		Assert.Equal(["!mp name \\\"a", "!mp close"], segments.Select(segment => segment.Text));
	}

	[Fact]
	public void UnclosedQuote_RestInSegment()
	{
		var segment = Assert.Single(CommandLine.Split("!mp name \"a;b"));

		Assert.Equal("!mp name \"a;b", segment.Text);
	}

	[Fact]
	public void Words_SplitOnSpaces()
	{
		Assert.Equal(["mp", "move", "Alice", "3"], CommandLine.Tokenize("mp  move Alice 3"));
	}

	[Fact]
	public void QuotedString_OneWordUnquoted()
	{
		Assert.Equal(["mp", "name", "My room"], CommandLine.Tokenize("mp name \"My room\""));
	}

	[Fact]
	public void EscapedQuote_OrdinaryCharacter()
	{
		Assert.Equal(["say", "\"hi"], CommandLine.Tokenize("say \\\"hi"));
		Assert.Equal(["say", "a\"b"], CommandLine.Tokenize("say \"a\\\"b\""));
	}

	[Fact]
	public void AnEscapedBackslash_IsOneBackslash()
	{
		Assert.Equal(["path", "a\\b"], CommandLine.Tokenize(@"path a\\b"));
	}

	[Fact]
	public void BackslashBeforeChar_Kept()
	{
		Assert.Equal(["path", "a\\b"], CommandLine.Tokenize("path a\\b"));
	}

	[Fact]
	public void UnclosedQuote_RestOneWord()
	{
		Assert.Equal(["mp", "name", "a b"], CommandLine.Tokenize("mp name \"a b"));
	}
}