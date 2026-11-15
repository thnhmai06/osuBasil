using Basil.Application.Beatmaps.Persistence;
using Basil.Infrastructure.Beatmaps.Logic;

namespace Basil.Infrastructure.Beatmaps.Tests.Logic;

/// <summary>The plain names the files of a beatmapset are stored under: distinct, and valid on every file system.</summary>
public sealed class StoredFileNamesTests
{
	private static BeatmapsetContentFile File(string name)
	{
		return new EmptyFile(name);
	}

	[Fact]
	public void NestedFilesWithTheSameName_GetNumberedNamesFromTwo()
	{
		BeatmapsetContentFile[] files = [File("a/bg.jpg"), File("b/bg.jpg"), File("c/bg.jpg")];

		var names = StoredFileNames.ChooseNames(files);

		Assert.Equal(["bg.jpg", "bg-2.jpg", "bg-3.jpg"], files.Select(file => names[file]));
	}

	[Fact]
	public void NamesDifferingOnlyInCase_AreNotTheSame()
	{
		BeatmapsetContentFile[] files = [File("bg.jpg"), File("BG.JPG")];

		var names = StoredFileNames.ChooseNames(files);

		Assert.Equal(["bg.jpg", "BG-2.JPG"], files.Select(file => names[file]));
	}

	[Fact]
	public void SameFileUsedTwice_KeepsOneName()
	{
		var file = File("bg.jpg");

		var names = StoredFileNames.ChooseNames([file, null, file]);

		Assert.Equal(["bg.jpg"], names.Values);
	}

	[Theory]
	[InlineData("CON", "_CON")]
	[InlineData("nul.txt", "_nul.txt")]
	[InlineData("COM1.png", "_COM1.png")]
	[InlineData("lpt9", "_lpt9")]
	[InlineData("CONOUT$.mp3", "_CONOUT$.mp3")]
	[InlineData("console.png", "console.png")]
	public void WindowsReservedName_IsPrefixed(string name, string expected)
	{
		Assert.Equal(expected, StoredFileNames.PlainName(name));
	}

	[Theory]
	[InlineData("a<b>c:d.png", "a_b_c_d.png")]
	[InlineData("dir/inner/name.mp3", "name.mp3")]
	[InlineData("trailing. .", "trailing")]
	[InlineData("...", "file")]
	public void InvalidCharacters_AreReplacedOrTrimmed(string name, string expected)
	{
		Assert.Equal(expected, StoredFileNames.PlainName(name));
	}

	private sealed class EmptyFile(string name) : BeatmapsetContentFile(name, 0)
	{
		public override Task<Stream> OpenAsync(CancellationToken ct = default)
		{
			return Task.FromResult<Stream>(new MemoryStream());
		}
	}
}
