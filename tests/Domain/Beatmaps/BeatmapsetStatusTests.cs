using Basil.Domain.Beatmaps;
using Basil.Domain.Game.Beatmaps;

namespace Basil.Domain.Tests.Beatmaps;

public class BeatmapStatusTests
{
	[Fact]
	public void EnumValues_MatchPython()
	{
		Assert.Equal(0, (int)BeatmapsetStatus.Pending);
		Assert.Equal(2, (int)BeatmapsetStatus.Ranked);
		Assert.Equal(3, (int)BeatmapsetStatus.Approved);
		Assert.Equal(4, (int)BeatmapsetStatus.Qualified);
		Assert.Equal(5, (int)BeatmapsetStatus.Loved);
	}

	[Theory]
	[InlineData(0, BeatmapsetStatus.Ranked)]
	[InlineData(2, BeatmapsetStatus.Pending)]
	[InlineData(3, BeatmapsetStatus.Qualified)]
	[InlineData(5, BeatmapsetStatus.Pending)]
	[InlineData(7, BeatmapsetStatus.Ranked)]
	[InlineData(8, BeatmapsetStatus.Loved)]
	public void FromOsuDirect_MatchesPythonMapping(int osuDirectStatus, BeatmapsetStatus expected)
	{
		Assert.Equal(expected, BeatmapsetStatusExtensions.FromOsuDirect(osuDirectStatus));
	}
}