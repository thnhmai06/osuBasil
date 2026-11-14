using Basil.Domain.Content;

namespace Basil.Domain.Tests.Content;

public class FaqEntryNameTests
{
	[Theory]
	[InlineData("rules")]
	[InlineData("help:irc")]
	[InlineData("a:b:c")]
	[InlineData("server rules")]
	[InlineData("folder/rules")]
	public void IsValid_PartsThatNameSomethingInAValidFile_IsTrue(string entry)
	{
		Assert.True(FaqEntryName.IsValid(entry));
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
		Assert.False(FaqEntryName.IsValid(entry));
	}

	[Fact]
	public void FileNameOf_ColonSeparatedParts_NamesFoldersAndATextFile()
	{
		Assert.Equal("help/irc.txt", FaqEntryName.FileNameOf("help:irc"));
	}
}