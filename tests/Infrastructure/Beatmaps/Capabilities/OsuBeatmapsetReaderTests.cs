using System.Text;
using Basil.Domain.Mechanics;
using Basil.Infrastructure.Beatmaps.Capabilities;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Beatmaps.Tests.Capabilities;

/// <summary>Pins what the reader takes from an archive and which archives it refuses.</summary>
public sealed class OsuBeatmapsetReaderTests
{
	/// <summary>A real standard difficulty, taken as the set's only difficulty.</summary>
	private static readonly byte[] Standard = OszArchive.Sample(Path.Combine("Beatmaps", "standard.osu"));

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Everything the archive declares reaches the content under the name the contract gives it.</summary>
	[Fact]
	public async Task ReadAsync_ReturnsWhatTheArchiveDeclares()
	{
		await using var archive = OszArchive.Build(
			("Camellia - Ghost.osu", Standard),
			("bg.jpg", [1, 2, 3]),
			("audio.mp3", [4, 5]),
			("set.osb", "<osb>"u8.ToArray()));
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.NotNull(content);
		await using (content)
		{
			Assert.Equal(1073074, content.OnlineSetId);
			Assert.Equal("Turbo", content.Artist);
			Assert.Equal("PADORU / PADORU", content.Title);
			Assert.Equal("Sotarks", content.Creator);

			var difficulty = Assert.Single(content.Difficulties);
			Assert.Equal(2245767, difficulty.OnlineId);
			Assert.Equal("Agatsu's Insane", difficulty.Version);
			Assert.Equal(GameMode.Standard, difficulty.Mode);
			Assert.Equal("Camellia - Ghost.osu", difficulty.File.Name);
			Assert.Equal("bg.jpg", difficulty.Background?.Name);
			Assert.Equal("audio.mp3", difficulty.Audio?.Name);
			Assert.Null(difficulty.Video);
			Assert.Equal(TimeSpan.FromMilliseconds(21142), difficulty.PreviewTime);
			Assert.False(difficulty.Storyboard);

			Assert.Equal(new[] { "Camellia - Ghost.osu", "bg.jpg", "audio.mp3", "set.osb" },
				content.Files.Select(file => file.Name));
			Assert.Equal("set.osb", content.Storyboard?.Name);
		}
	}

	/// <summary>A file of the set stays readable until the content is disposed, and not afterwards.</summary>
	[Fact]
	public async Task ReadAsync_KeepsTheFilesReadableUntilTheContentIsDisposed()
	{
		await using var archive = OszArchive.Build(
			("Camellia - Ghost.osu", Standard),
			("bg.jpg", [1, 2, 3]));
		var reader = new OsuBeatmapsetReader();
		var content = await reader.ReadAsync(archive, Token);
		Assert.NotNull(content);

		var background = content.Files.Single(file => file.Name == "bg.jpg");
		await using (var stream = await background.OpenAsync(Token))
		{
			using var buffer = new MemoryStream();
			await stream.CopyToAsync(buffer, Token);
			Assert.Equal(new byte[] { 1, 2, 3 }, buffer.ToArray());
		}

		await content.DisposeAsync();

		await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => background.OpenAsync(Token));
	}

	/// <summary>Content that is not an archive is not a beatmapset.</summary>
	[Fact]
	public async Task ReadAsync_ReturnsNullWhenTheArchiveIsNotAnArchive()
	{
		await using var archive = new MemoryStream("this is not a zip"u8.ToArray());
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.Null(content);
	}

	/// <summary>An archive with files but no difficulty is not a beatmapset.</summary>
	[Fact]
	public async Task ReadAsync_ReturnsNullWhenTheArchiveHasNoDifficulty()
	{
		await using var archive = OszArchive.Build(("audio.mp3", [1, 2, 3]), ("bg.jpg", [4, 5]));
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.Null(content);
	}

	/// <summary>A difficultly that cannot be decoded is skipped, and the readable ones still count.</summary>
	[Fact]
	public async Task ReadAsync_SkipsADifficultyItCannotDecode()
	{
		await using var archive = OszArchive.Build(
			("broken.osu", []),
			("Camellia - Ghost.osu", Standard));
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.NotNull(content);
		await using (content)
		{
			var difficulty = Assert.Single(content.Difficulties);
			Assert.Equal("Camellia - Ghost.osu", difficulty.File.Name);
		}
	}

	/// <summary>Difficulties lie at the top of a set; one deeper in a folder is no difficulty of the set.</summary>
	[Fact]
	public async Task ReadAsync_IgnoresADifficultyThatSitsInAFolder()
	{
		await using var archive = OszArchive.Build(("nested/Camellia - Ghost.osu", Standard));
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.Null(content);
	}

	/// <summary>A file named outside the set makes the whole archive unacceptable.</summary>
	/// <param name="name">The name of the file that leaves the set.</param>
	[Theory]
	[InlineData("../outside.jpg")]
	[InlineData("nested/../../outside.jpg")]
	[InlineData("/outside.jpg")]
	[InlineData("C:/outside.jpg")]
	[InlineData("nested//outside.jpg")]
	public async Task ReadAsync_ReturnsNullWhenAFileIsNamedOutsideTheSet(string name)
	{
		await using var archive = OszArchive.Build(("Camellia - Ghost.osu", Standard), (name, [1]));
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.Null(content);
	}

	/// <summary>Two files whose names differ only in case name the same file.</summary>
	[Fact]
	public async Task ReadAsync_ReturnsNullWhenTwoFilesShareANameIgnoringCase()
	{
		await using var archive = OszArchive.Build(
			("Camellia - Ghost.osu", Standard),
			("bg.jpg", [1]),
			("BG.JPG", [2]));
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.Null(content);
	}

	/// <summary>The largest number of files a set may hold is accepted.</summary>
	[Fact]
	public async Task ReadAsync_AcceptsTheLargestAllowedSet()
	{
		await using var archive = OszArchive.Build([("Camellia - Ghost.osu", Standard), .. Filler(1_999)]);
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.NotNull(content);
		await content.DisposeAsync();
	}

	/// <summary>One file more than a set may hold makes the archive unacceptable.</summary>
	[Fact]
	public async Task ReadAsync_ReturnsNullPastTheLargestAllowedSet()
	{
		await using var archive = OszArchive.Build([("Camellia - Ghost.osu", Standard), .. Filler(2_000)]);
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.Null(content);
	}

	/// <summary>A difficulty past the size a difficulty may take is skipped.</summary>
	[Fact]
	public async Task ReadAsync_SkipsADifficultyPastTheAllowedSize()
	{
		var oversized = new byte[16 * 1024 * 1024 + 1];
		await using var archive = OszArchive.Build(("huge.osu", oversized));
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.Null(content);
	}

	/// <summary>Blank metadata takes the name the domain accepts in place of a blank one.</summary>
	[Fact]
	public async Task ReadAsync_UsesUnknownForBlankMetadata()
	{
		var text = Encoding.UTF8.GetString(Standard)
			.Replace("Artist:Turbo", "Artist: ", StringComparison.Ordinal)
			.Replace("Title:PADORU / PADORU", "Title: ", StringComparison.Ordinal)
			.Replace("Creator:Sotarks", "Creator: ", StringComparison.Ordinal);
		await using var archive = OszArchive.Build(("blank.osu", Encoding.UTF8.GetBytes(text)));
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.NotNull(content);
		await using (content)
		{
			Assert.Equal("Unknown", content.Artist);
			Assert.Equal("Unknown", content.Title);
			Assert.Equal("Unknown", content.Creator);
		}
	}

	/// <summary>A difficulty that sets no preview time has none.</summary>
	[Fact]
	public async Task ReadAsync_OmitsThePreviewTimeTheDifficultyDoesNotSet()
	{
		var text = Encoding.UTF8.GetString(Standard)
			.Replace("PreviewTime:21142", "PreviewTime:-1", StringComparison.Ordinal);
		await using var archive = OszArchive.Build(("nopreview.osu", Encoding.UTF8.GetBytes(text)));
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.NotNull(content);
		await using (content)
		{
			var difficulty = Assert.Single(content.Difficulties);
			Assert.Null(difficulty.PreviewTime);
		}
	}

	/// <summary>A difficulty that names a storyboard object draws one.</summary>
	[Fact]
	public async Task ReadAsync_ReportsADifficultyThatDrawsAStoryboard()
	{
		var text = Encoding.UTF8.GetString(Standard)
			.Replace("//Storyboard Layer 0 (Background)",
				"Sprite,Foreground,Centre,\"sb/arrow.png\",320,240", StringComparison.Ordinal);
		await using var archive = OszArchive.Build(("storyboard.osu", Encoding.UTF8.GetBytes(text)));
		var reader = new OsuBeatmapsetReader();

		var content = await reader.ReadAsync(archive, Token);

		Assert.NotNull(content);
		await using (content)
		{
			var difficulty = Assert.Single(content.Difficulties);
			Assert.True(difficulty.Storyboard);
		}
	}

	/// <summary>Builds small files that only fill the set up to the file-count limit.</summary>
	/// <param name="count">How many files to build.</param>
	/// <returns>The files, with distinct names.</returns>
	private static (string Name, byte[] Content)[] Filler(int count)
	{
		var files = new (string, byte[])[count];
		for (var index = 0; index < count; index++) files[index] = ($"fill{index}.txt", [0]);
		return files;
	}
}