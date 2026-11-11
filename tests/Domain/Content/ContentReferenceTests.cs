using Basil.Domain.Content;

namespace Basil.Domain.Tests.Content;

/// <summary>Verifies the rules of the canonical reference to a stored or hosted asset.</summary>
public class ContentReferenceTests
{
	[Theory]
	[InlineData("CON")]
	[InlineData("con")]
	[InlineData("NUL.txt")]
	[InlineData("COM1")]
	[InlineData("LPT9.log")]
	[InlineData("aux")]
	[InlineData("menu/CON")]
	public void Path_WindowsDeviceName_Throws(string path)
	{
		Assert.Throws<ArgumentException>(() => new ContentReference(path));
	}

	[Theory]
	[InlineData("folder.")]
	[InlineData("folder /file")]
	[InlineData("folder/file.")]
	public void Path_SegmentEndingWithDotOrSpace_Throws(string path)
	{
		Assert.Throws<ArgumentException>(() => new ContentReference(path));
	}

	[Theory]
	[InlineData("a<b")]
	[InlineData("a>b")]
	[InlineData("a\"b")]
	[InlineData("a|b")]
	[InlineData("a?b")]
	[InlineData("a*b")]
	public void Path_ForbiddenCharacter_Throws(string path)
	{
		Assert.Throws<ArgumentException>(() => new ContentReference(path));
	}

	[Fact]
	public void Parse_Whitespace_ThrowsFormatException()
	{
		Assert.Throws<FormatException>(() => ContentReference.Parse(" ", null));
	}

	[Fact]
	public void ToString_UnsupportedFormat_ThrowsFormatException()
	{
		var reference = new ContentReference("banner.png");

		Assert.Throws<FormatException>(() => reference.ToString("X", null));
	}

	[Fact]
	public void ToString_GeneralFormat_ReturnsCanonicalText()
	{
		var reference = new ContentReference("banner.png");

		Assert.Equal("banner.png", reference.ToString("G", null));
		Assert.Equal("banner.png", reference.ToString(null, null));
		Assert.Equal("banner.png", reference.ToString("", null));
	}

	[Theory]
	[InlineData("banner.png")]
	[InlineData("Menu/Banners/a b.png")]
	[InlineData("folder/file.txt")]
	[InlineData("..hidden")]
	[InlineData("a..b")]
	[InlineData("COM0")]
	[InlineData("COM10")]
	[InlineData("CONSOLE.txt")]
	public void IsValidPath_CanonicalRelativePath_IsTrue(string path)
	{
		Assert.True(ContentReference.IsValidPath(path));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("/rooted.png")]
	[InlineData("a\\b.png")]
	[InlineData("a//b.png")]
	[InlineData("a/")]
	[InlineData("./a.png")]
	[InlineData("a/./b.png")]
	[InlineData("../a.png")]
	[InlineData("a/../b.png")]
	[InlineData("a:b.png")]
	[InlineData("C:file.png")]
	[InlineData("a\0b.png")]
	[InlineData("a\tb.png")]
	[InlineData(" a.png")]
	[InlineData("a.png ")]
	[InlineData("dir/file.")]
	[InlineData("dir/file ")]
	[InlineData("a<b.png")]
	[InlineData("a>b.png")]
	[InlineData("a\"b.png")]
	[InlineData("a|b.png")]
	[InlineData("a?b.png")]
	[InlineData("a*b.png")]
	[InlineData("CON")]
	[InlineData("con.txt")]
	[InlineData("dir/NUL.png")]
	[InlineData("AUX")]
	[InlineData("PRN")]
	[InlineData("COM1")]
	[InlineData("LPT9.txt")]
	public void IsValidPath_EachBrokenRule_IsFalse(string? path)
	{
		Assert.False(ContentReference.IsValidPath(path));
	}
}