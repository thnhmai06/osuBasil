using Basil.Infrastructure.Common.Persistence;

namespace Basil.Infrastructure.Common.Tests.Persistence;

/// <summary>Pins that a LIKE pattern built from a user's text matches that text literally.</summary>
public sealed class LikePatternTests
{
	/// <summary>The characters LIKE gives a meaning to are escaped; every other character is left as it is.</summary>
	[Theory]
	[InlineData("plain", "plain")]
	[InlineData("a_b", @"a\_b")]
	[InlineData("100%", @"100\%")]
	[InlineData(@"a\b", @"a\\b")]
	[InlineData(@"a_b%c\d", @"a\_b\%c\\d")]
	[InlineData("", "")]
	public void Literal_EscapesTheWildcardsOfLike(string text, string expected)
	{
		Assert.Equal(expected, LikePattern.Literal(text));
	}

	/// <summary>A pattern that finds a part of a value is the literal text between two percent signs.</summary>
	[Fact]
	public void Containing_WrapsTheLiteralTextInPercentSigns()
	{
		Assert.Equal(@"%a\_b%", LikePattern.Containing("a_b"));
	}
}