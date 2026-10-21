using Basil.Bot.Commands;

namespace Basil.Bot.Tests.Commands;

/// <summary>
///     The syntax of a command message: segments chained by <c>;</c> and <c>&amp;&amp;</c>, words split on spaces, a
///     double quote that groups words, and a backslash that makes a following quote or backslash an ordinary character.
/// </summary>
public sealed class CommandLineTests
{
	[Fact]
	public void Split_AMessageWithoutOperators_IsOneSegment()
	{
		var segments = CommandLine.Split("!mp settings");

		var segment = Assert.Single(segments);
		Assert.Equal("!mp settings", segment.Text);
		Assert.Equal(ChainOperator.Semicolon, segment.Operator);
	}

	[Fact]
	public void Split_ASemicolon_StartsASegmentThatAlwaysRuns()
	{
		var segments = CommandLine.Split("!mp lock;!mp unlock");

		Assert.Equal(["!mp lock", "!mp unlock"], segments.Select(segment => segment.Text));
		Assert.Equal(ChainOperator.Semicolon, segments[1].Operator);
	}

	[Fact]
	public void Split_ADoubleAmpersand_StartsASegmentThatRunsOnlyAfterSuccess()
	{
		var segments = CommandLine.Split("!mp lock && !mp unlock");

		Assert.Equal(["!mp lock", "!mp unlock"], segments.Select(segment => segment.Text));
		Assert.Equal(ChainOperator.And, segments[1].Operator);
	}

	[Fact]
	public void Split_ASingleAmpersand_IsNotAnOperator()
	{
		var segment = Assert.Single(CommandLine.Split("!mp name A & B"));

		Assert.Equal("!mp name A & B", segment.Text);
	}

	[Fact]
	public void Split_EmptySegments_AreLeftOut()
	{
		var segments = CommandLine.Split("!mp lock;; ;!mp unlock");

		Assert.Equal(["!mp lock", "!mp unlock"], segments.Select(segment => segment.Text));
	}

	[Fact]
	public void Split_AnOperatorInsideQuotes_DoesNotSplit()
	{
		var segment = Assert.Single(CommandLine.Split("!mp name \"a; b && c\""));

		Assert.Equal("!mp name \"a; b && c\"", segment.Text);
	}

	[Fact]
	public void Split_AnEscapedQuoteInsideQuotes_DoesNotEndThem()
	{
		var segment = Assert.Single(CommandLine.Split("!mp name \"a\\\";b\""));

		Assert.Equal("!mp name \"a\\\";b\"", segment.Text);
	}

	[Fact]
	public void Split_AnEscapedQuoteOutsideQuotes_DoesNotStartThem()
	{
		var segments = CommandLine.Split("!mp name \\\"a;!mp close");

		Assert.Equal(["!mp name \\\"a", "!mp close"], segments.Select(segment => segment.Text));
	}

	[Fact]
	public void Split_AQuoteThatNeverEnds_KeepsTheRestInTheSegment()
	{
		var segment = Assert.Single(CommandLine.Split("!mp name \"a;b"));

		Assert.Equal("!mp name \"a;b", segment.Text);
	}

	[Fact]
	public void Tokenize_Words_AreSplitOnSpaces()
	{
		Assert.Equal(["mp", "move", "Alice", "3"], CommandLine.Tokenize("mp  move Alice 3"));
	}

	[Fact]
	public void Tokenize_AQuotedString_IsOneWordWithoutItsQuotes()
	{
		Assert.Equal(["mp", "name", "My room"], CommandLine.Tokenize("mp name \"My room\""));
	}

	[Fact]
	public void Tokenize_AnEscapedQuote_IsAnOrdinaryCharacter()
	{
		Assert.Equal(["say", "\"hi"], CommandLine.Tokenize("say \\\"hi"));
		Assert.Equal(["say", "a\"b"], CommandLine.Tokenize("say \"a\\\"b\""));
	}

	[Fact]
	public void Tokenize_AnEscapedBackslash_IsOneBackslash()
	{
		Assert.Equal(["path", "a\\b"], CommandLine.Tokenize("path a\\\\b"));
	}

	[Fact]
	public void Tokenize_ABackslashBeforeAnotherCharacter_IsKept()
	{
		Assert.Equal(["path", "a\\b"], CommandLine.Tokenize("path a\\b"));
	}

	[Fact]
	public void Tokenize_AQuoteThatNeverEnds_TakesTheRestAsOneWord()
	{
		Assert.Equal(["mp", "name", "a b"], CommandLine.Tokenize("mp name \"a b"));
	}
}
