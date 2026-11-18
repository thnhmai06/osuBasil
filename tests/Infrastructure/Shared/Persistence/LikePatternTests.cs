using Basil.Infrastructure.Shared.Persistence;

namespace Basil.Infrastructure.Shared.Tests.Persistence;

public sealed class LikePatternTests
{
	[Theory]
	[InlineData("plain", "plain")]
	[InlineData("a_b", "a!_b")]
	[InlineData("100%", "100!%")]
	[InlineData("a!b", "a!!b")]
	[InlineData("a_b%c!d", "a!_b!%c!!d")]
	[InlineData("", "")]
	public void Literal_EscapesTheWildcardsOfLike(string text, string expected)
	{
		Assert.Equal(expected, LikePattern.Literal(text));
	}

	[Fact]
	public void Containing_WrapsTheLiteralTextInPercentSigns()
	{
		Assert.Equal("%a!_b%", LikePattern.Containing("a_b"));
	}
}