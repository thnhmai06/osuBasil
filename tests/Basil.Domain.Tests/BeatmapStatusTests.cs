using Basil.Domain.Beatmaps;

namespace Basil.Domain.Tests;

/// <summary>Ported from app/constants/beatmap_statuses.py's BeatmapStatus.</summary>
public class BeatmapStatusTests
{
	[Fact]
	public void EnumValues_MatchPython()
	{
		Assert.Equal(-1, (int)BeatmapStatus.NotSubmitted);
		Assert.Equal(0, (int)BeatmapStatus.Pending);
		Assert.Equal(1, (int)BeatmapStatus.UpdateAvailable);
		Assert.Equal(2, (int)BeatmapStatus.Ranked);
		Assert.Equal(3, (int)BeatmapStatus.Approved);
		Assert.Equal(4, (int)BeatmapStatus.Qualified);
		Assert.Equal(5, (int)BeatmapStatus.Loved);
	}

	[Theory]
	[InlineData(BeatmapStatus.Pending, 0)]
	[InlineData(BeatmapStatus.Ranked, 1)]
	[InlineData(BeatmapStatus.Approved, 2)]
	[InlineData(BeatmapStatus.Qualified, 3)]
	[InlineData(BeatmapStatus.Loved, 4)]
	public void OsuApi_MatchesPythonMapping(BeatmapStatus status, int expected)
	{
		Assert.Equal(expected, status.ToOsuApi());
	}

	[Fact]
	public void OsuApi_UnmappedStatus_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => BeatmapStatus.NotSubmitted.ToOsuApi());
		Assert.Throws<ArgumentOutOfRangeException>(() => BeatmapStatus.UpdateAvailable.ToOsuApi());
	}

	[Theory]
	[InlineData(-2, BeatmapStatus.Pending)]
	[InlineData(-1, BeatmapStatus.Pending)]
	[InlineData(0, BeatmapStatus.Pending)]
	[InlineData(1, BeatmapStatus.Ranked)]
	[InlineData(2, BeatmapStatus.Approved)]
	[InlineData(3, BeatmapStatus.Qualified)]
	[InlineData(4, BeatmapStatus.Loved)]
	[InlineData(999, BeatmapStatus.UpdateAvailable)]
	public void FromOsuApi_MatchesPythonMapping(int osuApiStatus, BeatmapStatus expected)
	{
		Assert.Equal(expected, RankedStatusExtensions.FromOsuApi(osuApiStatus));
	}

	[Theory]
	[InlineData(0, BeatmapStatus.Ranked)]
	[InlineData(2, BeatmapStatus.Pending)]
	[InlineData(3, BeatmapStatus.Qualified)]
	[InlineData(5, BeatmapStatus.Pending)]
	[InlineData(7, BeatmapStatus.Ranked)]
	[InlineData(8, BeatmapStatus.Loved)]
	[InlineData(999, BeatmapStatus.UpdateAvailable)]
	public void FromOsuDirect_MatchesPythonMapping(int osuDirectStatus, BeatmapStatus expected)
	{
		Assert.Equal(expected, RankedStatusExtensions.FromOsuDirect(osuDirectStatus));
	}
}