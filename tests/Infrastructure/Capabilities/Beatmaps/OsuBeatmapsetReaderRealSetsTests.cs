using Basil.Domain.Mechanics;
using Basil.Infrastructure.Capabilities.Beatmaps;
using Basil.Infrastructure.Capabilities.Tests.Support;

namespace Basil.Infrastructure.Capabilities.Tests.Beatmaps;

/// <summary>
///     Pins what the reader takes from archives of real, ranked sets. The expected values are the metadata the
///     <c>.osu</c> files of each set declare, written by hand from those files.
/// </summary>
public sealed class OsuBeatmapsetReaderRealSetsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static MemoryStream Open(string name)
	{
		return new MemoryStream(OszArchive.Sample(Path.Combine("Sets", name)), false);
	}

	/// <summary>A set of twenty-six standard difficulties gives one difficulty per file, all in standard.</summary>
	[Fact]
	public async Task ReadAsync_AStandardSetGivesEveryDifficultyItHas()
	{
		await using var content = await new OsuBeatmapsetReader().ReadAsync(Open("padoru-standard.osz"), Token);

		Assert.NotNull(content);
		Assert.Equal(1073074, content.OnlineSetId);
		Assert.Equal("Turbo", content.Artist);
		Assert.Equal("PADORU / PADORU", content.Title);
		Assert.Equal("Sotarks", content.Creator);
		Assert.Equal(26, content.Difficulties.Count);
		Assert.All(content.Difficulties, difficulty => Assert.Equal(GameMode.Standard, difficulty.Mode));
		Assert.Equal(26, content.Difficulties.Select(difficulty => difficulty.Version).Distinct().Count());
		Assert.Contains(content.Difficulties, difficulty => difficulty.OnlineId == 2245767 && difficulty.Version == "Agatsu's Insane");
		Assert.All(content.Difficulties, difficulty =>
		{
			Assert.Equal("audio.mp3", difficulty.Audio?.Name);
			Assert.Equal("bg.jpg", difficulty.Background?.Name);
		});
	}

	/// <summary>A catch set reports its mode, the files it names and its preview point.</summary>
	[Fact]
	public async Task ReadAsync_ACatchSetReportsItsModeAndItsFiles()
	{
		await using var content = await new OsuBeatmapsetReader().ReadAsync(Open("hopes-and-dreams-catch.osz"), Token);

		Assert.NotNull(content);
		Assert.Equal(2585524, content.OnlineSetId);
		Assert.Equal("Garoad", content.Artist);
		Assert.Equal("Hopes and Dreams", content.Title);
		Assert.Equal("bighzonda", content.Creator);
		Assert.Equal(new[] { "Calm", "Serenity" }, content.Difficulties.Select(difficulty => difficulty.Version).Order());
		Assert.All(content.Difficulties, difficulty =>
		{
			Assert.Equal(GameMode.Catch, difficulty.Mode);
			Assert.Equal("audio.mp3", difficulty.Audio?.Name);
			Assert.Equal("bg.jpg", difficulty.Background?.Name);
			Assert.Equal(TimeSpan.FromMilliseconds(20381), difficulty.PreviewTime);
			Assert.Null(difficulty.Video);
			Assert.False(difficulty.Storyboard);
		});
	}

	/// <summary>A taiko set whose audio file is missing from the archive still reads; the missing file is left out.</summary>
	[Fact]
	public async Task ReadAsync_ATaikoSetWithoutItsAudioStillReads()
	{
		await using var content = await new OsuBeatmapsetReader().ReadAsync(Open("mosi-mosi-taiko.osz"), Token);

		Assert.NotNull(content);
		Assert.Equal(2583478, content.OnlineSetId);
		Assert.Equal("BANIO", content.Artist);
		Assert.Equal("Faputa", content.Creator);
		Assert.Equal(5, content.Difficulties.Count);
		Assert.All(content.Difficulties, difficulty =>
		{
			Assert.Equal(GameMode.Taiko, difficulty.Mode);
			Assert.Null(difficulty.Audio);
			Assert.Equal("mosbur.jpg", difficulty.Background?.Name);
		});
	}

	/// <summary>A mania set reports its mode and the creator's name with its spaces.</summary>
	[Fact]
	public async Task ReadAsync_AManiaSetReportsItsModeAndItsCreator()
	{
		await using var content = await new OsuBeatmapsetReader().ReadAsync(Open("selene-ui-mania.osz"), Token);

		Assert.NotNull(content);
		Assert.Equal(2562207, content.OnlineSetId);
		Assert.Equal("Kou!", content.Artist);
		Assert.Equal("S k y y -", content.Creator);
		var difficulty = Assert.Single(content.Difficulties);
		Assert.Equal(GameMode.Mania, difficulty.Mode);
		Assert.Equal(5697955, difficulty.OnlineId);
		Assert.Equal("Nightfall.exe", difficulty.Version);
	}
}
