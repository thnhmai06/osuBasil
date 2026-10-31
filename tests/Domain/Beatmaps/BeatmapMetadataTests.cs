using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;

namespace Basil.Domain.Tests.Beatmaps;

/// <summary>Verifies the validation of the metadata of one difficulty of a beatmapset.</summary>
public class BeatmapMetadataTests
{
	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t")]
	public void Version_EmptyOrWhitespace_Throws(string version)
	{
		Assert.Throws<ArgumentException>(() => new BeatmapMetadata
		{
			Version = version,
			Difficulty = MakeDifficulty(),
			Objects = new OsuObjects()
		});
	}

	[Fact]
	public void Version_Valid_IsAccepted()
	{
		var metadata = new BeatmapMetadata
		{
			Version = "Hyper",
			Difficulty = MakeDifficulty(),
			Objects = new OsuObjects()
		};

		Assert.Equal("Hyper", metadata.Version);
	}

	private static BeatmapDifficulty MakeDifficulty()
	{
		return new BeatmapDifficulty(GameMode.Standard, 180.0, TimeSpan.FromSeconds(120), 4.0, 9.0, 8.0, 5.0, 6.5);
	}
}