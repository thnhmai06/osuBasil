using Basil.Domain.Beatmaps;

namespace Basil.Domain.Tests;

/// <summary>Verifies `BeatmapsetStatus`'s integer enum values and its `ToOsuApi()` mapping.</summary>
public class BeatmapStatusTests
{
	[Fact]
	public void EnumValues_MatchPython()
	{
		Assert.Equal(-1, (int)BeatmapsetStatus.NotSubmitted);
		Assert.Equal(0, (int)BeatmapsetStatus.Pending);
		Assert.Equal(1, (int)BeatmapsetStatus.UpdateAvailable);
		Assert.Equal(2, (int)BeatmapsetStatus.Ranked);
		Assert.Equal(3, (int)BeatmapsetStatus.Approved);
		Assert.Equal(4, (int)BeatmapsetStatus.Qualified);
		Assert.Equal(5, (int)BeatmapsetStatus.Loved);
	}

	[Theory]
	[InlineData(BeatmapsetStatus.Pending, 0)]
	[InlineData(BeatmapsetStatus.Ranked, 1)]
	[InlineData(BeatmapsetStatus.Approved, 2)]
	[InlineData(BeatmapsetStatus.Qualified, 3)]
	[InlineData(BeatmapsetStatus.Loved, 4)]
	public void OsuApi_MatchesPythonMapping(BeatmapsetStatus status, int expected)
	{
		Assert.Equal(expected, status.ToOsuApi());
	}

	[Fact]
	public void OsuApi_UnmappedStatus_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => BeatmapsetStatus.NotSubmitted.ToOsuApi());
		Assert.Throws<ArgumentOutOfRangeException>(() => BeatmapsetStatus.UpdateAvailable.ToOsuApi());
	}

	[Theory]
	[InlineData(-2, BeatmapsetStatus.Pending)]
	[InlineData(-1, BeatmapsetStatus.Pending)]
	[InlineData(0, BeatmapsetStatus.Pending)]
	[InlineData(1, BeatmapsetStatus.Ranked)]
	[InlineData(2, BeatmapsetStatus.Approved)]
	[InlineData(3, BeatmapsetStatus.Qualified)]
	[InlineData(4, BeatmapsetStatus.Loved)]
	[InlineData(999, BeatmapsetStatus.UpdateAvailable)]
	public void FromOsuApi_MatchesPythonMapping(int osuApiStatus, BeatmapsetStatus expected)
	{
		Assert.Equal(expected, BeatmapsetStatusExtensions.FromOsuApi(osuApiStatus));
	}

	[Theory]
	[InlineData(0, BeatmapsetStatus.Ranked)]
	[InlineData(2, BeatmapsetStatus.Pending)]
	[InlineData(3, BeatmapsetStatus.Qualified)]
	[InlineData(5, BeatmapsetStatus.Pending)]
	[InlineData(7, BeatmapsetStatus.Ranked)]
	[InlineData(8, BeatmapsetStatus.Loved)]
	[InlineData(999, BeatmapsetStatus.UpdateAvailable)]
	public void FromOsuDirect_MatchesPythonMapping(int osuDirectStatus, BeatmapsetStatus expected)
	{
		Assert.Equal(expected, BeatmapsetStatusExtensions.FromOsuDirect(osuDirectStatus));
	}
}