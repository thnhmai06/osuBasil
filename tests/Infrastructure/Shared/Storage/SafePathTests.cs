using Basil.Infrastructure.Shared.Storage;

namespace Basil.Infrastructure.Shared.Tests.Storage;

/// <summary>Contract tests of SafePath: a name is combined with the root only when it stays inside it.</summary>
public sealed class SafePathTests
{
	private static readonly string Root = Path.Combine(Path.GetTempPath(), "basil-safepath-root");

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t")]
	public void BlankName_Refused(string name)
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
	public void DotSegment_Refused(string name)
	{
		Assert.Throws<ArgumentException>(() => SafePath.Combine(Root, name));
	}

	[Theory]
	[InlineData("/rooted.txt")]
	[InlineData(@"\rooted.txt")]
	[InlineData(@"\\server\share\file.txt")]
	public void RootedName_Refused(string name)
	{
		Assert.Throws<ArgumentException>(() => SafePath.Combine(Root, name));
	}

	[Theory]
	[InlineData(@"C:\file.txt")]
	[InlineData("C:file.txt")]
	[InlineData("a:b")]
	public void DriveLetterName_RefusedOnWindows(string name)
	{
		Assert.SkipUnless(OperatingSystem.IsWindows(), "A drive letter names a drive only on Windows.");

		Assert.Throws<ArgumentException>(() => SafePath.Combine(Root, name));
	}

	[Theory]
	[InlineData("a:b")]
	[InlineData("a//b.txt")]
	[InlineData("dir/file.")]
	[InlineData("CON.txt")]
	[InlineData(" file.txt")]
	public void NameNoAssetReferenceCanRefer_RefusedOnEveryPlatform(string name)
	{
		Assert.Throws<ArgumentException>(() => SafePath.Combine(Root, name));
	}

	[Fact]
	public void NullCharacterName_Refused()
	{
		Assert.Throws<ArgumentException>(() => SafePath.Combine(Root, "file\0.txt"));
	}

	[Theory]
	[InlineData("file.txt")]
	[InlineData("a b.txt")]
	[InlineData("..hidden")]
	[InlineData("a..b")]
	public void PlainName_PlacedInRoot(string name)
	{
		Assert.Equal(Path.Combine(Root, name), SafePath.Combine(Root, name));
	}

	[Fact]
	public void FolderedName_PlacedUnderRoot()
	{
		Assert.Equal(Path.Combine(Root, "folder", "file.txt"), SafePath.Combine(Root, "folder/file.txt"));
	}

	[Fact]
	public void TrailingSeparatorRoot_SamePlacement()
	{
		var withSeparator = Root + Path.DirectorySeparatorChar;

		Assert.Equal(Path.Combine(Root, "file.txt"), SafePath.Combine(withSeparator, "file.txt"));
	}
}