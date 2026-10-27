using Basil.Common.Infrastructure.Files;

namespace Basil.Common.Infrastructure.Tests;

/// <summary>Contract tests of SafePath: a name is combined with the root only when it stays inside it.</summary>
public sealed class SafePathTests
{
	private static readonly string Root = Path.Combine(Path.GetTempPath(), "basil-safepath-root");

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t")]
	public void A_blank_name_is_refused(string name)
	{
		Assert.ThrowsAny<ArgumentException>(() => SafePath.Combine(Root, name));
	}

	[Theory]
	[InlineData(".")]
	[InlineData("..")]
	[InlineData("../escape.txt")]
	[InlineData(@"..\escape.txt")]
	[InlineData("folder/../escape.txt")]
	[InlineData(@"folder\..\escape.txt")]
	[InlineData("folder/../../escape.txt")]
	[InlineData("./file.txt")]
	[InlineData("folder/./file.txt")]
	[InlineData("folder/..")]
	public void A_name_with_a_dot_or_dot_dot_segment_is_refused(string name)
	{
		Assert.Throws<ArgumentException>(() => SafePath.Combine(Root, name));
	}

	[Theory]
	[InlineData("/rooted.txt")]
	[InlineData(@"\rooted.txt")]
	[InlineData(@"\\server\share\file.txt")]
	public void A_rooted_name_is_refused(string name)
	{
		Assert.Throws<ArgumentException>(() => SafePath.Combine(Root, name));
	}

	[Theory]
	[InlineData(@"C:\file.txt")]
	[InlineData("C:file.txt")]
	[InlineData("a:b")]
	public void A_name_with_a_drive_letter_is_refused_on_Windows(string name)
	{
		Assert.SkipUnless(OperatingSystem.IsWindows(), "A drive letter names a drive only on Windows.");

		Assert.Throws<ArgumentException>(() => SafePath.Combine(Root, name));
	}

	[Fact]
	public void A_name_with_a_null_character_is_refused()
	{
		Assert.Throws<ArgumentException>(() => SafePath.Combine(Root, "file\0.txt"));
	}

	[Theory]
	[InlineData("file.txt")]
	[InlineData("a b.txt")]
	[InlineData("..hidden")]
	[InlineData("a..b")]
	public void A_plain_name_is_placed_in_the_root(string name)
	{
		Assert.Equal(Path.Combine(Root, name), SafePath.Combine(Root, name));
	}

	[Fact]
	public void A_name_with_folders_is_placed_under_them_in_the_root()
	{
		Assert.Equal(Path.Combine(Root, "folder", "file.txt"), SafePath.Combine(Root, "folder/file.txt"));
	}

	[Fact]
	public void A_root_with_a_trailing_separator_places_names_the_same()
	{
		var withSeparator = Root + Path.DirectorySeparatorChar;

		Assert.Equal(Path.Combine(Root, "file.txt"), SafePath.Combine(withSeparator, "file.txt"));
	}
}