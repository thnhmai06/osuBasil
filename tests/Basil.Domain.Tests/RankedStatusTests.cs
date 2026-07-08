using Basil.Domain.Beatmaps;

namespace Basil.Domain.Tests;

/// <summary>Ported from app/constants/beatmap_statuses.py's RankedStatus.</summary>
public class RankedStatusTests
{
    [Fact]
    public void EnumValues_MatchPython()
    {
        Assert.Equal(-1, (int)RankedStatus.NotSubmitted);
        Assert.Equal(0, (int)RankedStatus.Pending);
        Assert.Equal(1, (int)RankedStatus.UpdateAvailable);
        Assert.Equal(2, (int)RankedStatus.Ranked);
        Assert.Equal(3, (int)RankedStatus.Approved);
        Assert.Equal(4, (int)RankedStatus.Qualified);
        Assert.Equal(5, (int)RankedStatus.Loved);
    }

    [Theory]
    [InlineData(RankedStatus.Pending, 0)]
    [InlineData(RankedStatus.Ranked, 1)]
    [InlineData(RankedStatus.Approved, 2)]
    [InlineData(RankedStatus.Qualified, 3)]
    [InlineData(RankedStatus.Loved, 4)]
    public void OsuApi_MatchesPythonMapping(RankedStatus status, int expected)
    {
        Assert.Equal(expected, status.OsuApi());
    }

    [Fact]
    public void OsuApi_UnmappedStatus_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RankedStatus.NotSubmitted.OsuApi());
        Assert.Throws<ArgumentOutOfRangeException>(() => RankedStatus.UpdateAvailable.OsuApi());
    }

    [Theory]
    [InlineData(-2, RankedStatus.Pending)]
    [InlineData(-1, RankedStatus.Pending)]
    [InlineData(0, RankedStatus.Pending)]
    [InlineData(1, RankedStatus.Ranked)]
    [InlineData(2, RankedStatus.Approved)]
    [InlineData(3, RankedStatus.Qualified)]
    [InlineData(4, RankedStatus.Loved)]
    [InlineData(999, RankedStatus.UpdateAvailable)]
    public void FromOsuApi_MatchesPythonMapping(int osuApiStatus, RankedStatus expected)
    {
        Assert.Equal(expected, RankedStatusExtensions.FromOsuApi(osuApiStatus));
    }

    [Theory]
    [InlineData(0, RankedStatus.Ranked)]
    [InlineData(2, RankedStatus.Pending)]
    [InlineData(3, RankedStatus.Qualified)]
    [InlineData(5, RankedStatus.Pending)]
    [InlineData(7, RankedStatus.Ranked)]
    [InlineData(8, RankedStatus.Loved)]
    [InlineData(999, RankedStatus.UpdateAvailable)]
    public void FromOsuDirect_MatchesPythonMapping(int osuDirectStatus, RankedStatus expected)
    {
        Assert.Equal(expected, RankedStatusExtensions.FromOsuDirect(osuDirectStatus));
    }
}