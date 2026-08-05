using Basil.Domain.Beatmaps;

namespace Basil.Domain.Tests;

public class BeatmapsetTests
{
	private static Beatmapset Make(int id)
	{
		return new Beatmapset(id, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow);
	}

	[Fact]
	public void IsLocallyIngested_IdBelowFloor_ReturnsFalse()
	{
		Assert.False(Make(Beatmap.LocalIdFloor - 1).IsLocallyIngested);
	}

	[Fact]
	public void IsLocallyIngested_IdAtFloor_ReturnsTrue()
	{
		Assert.True(Make(Beatmap.LocalIdFloor).IsLocallyIngested);
	}

	[Fact]
	public void IsLocallyIngested_IdAboveFloor_ReturnsTrue()
	{
		Assert.True(Make(Beatmap.LocalIdFloor + 1).IsLocallyIngested);
	}
}