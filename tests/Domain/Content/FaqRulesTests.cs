using Basil.Domain.Content;

namespace Basil.Domain.Tests.Content;

public class FaqRulesTests
{
	[Theory]
	[InlineData("rules")]
	[InlineData("help:irc")]
	[InlineData("a:b:c")]
	[InlineData("server rules")]
	[InlineData("folder/rules")]
	public void IsValid_PartsThatNameSomethingInAValidFile_IsTrue(string entry)
	{
		Assert.True(FaqRules.IsValidName(entry));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("  ")]
	[InlineData(":")]
	[InlineData("a::b")]
	[InlineData(":a")]
	[InlineData("a:")]
	[InlineData("a: ")]
	[InlineData("../x")]
	[InlineData("/x")]
	[InlineData("a\\b")]
	[InlineData("a\0b")]
	[InlineData("CON")]
	[InlineData("Rules:CON")]
	public void IsValid_EachBrokenRule_IsFalse(string? entry)
	{
		Assert.False(FaqRules.IsValidName(entry));
	}

	[Fact]
	public void FileNameOf_ColonSeparatedParts_NamesFoldersAndATextFile()
	{
		Assert.Equal("help/irc.txt", FaqRules.FileNameOf("help:irc"));
	}
}