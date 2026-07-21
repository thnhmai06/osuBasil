using Basil.Domain.Login;

namespace Basil.Domain.Tests;

/// <summary>
///     Ported from app/constants/regexes.py's OSU_VERSION + app/api/domains/cho.py's
///     parse_osu_version_string. Reference behaviour verified by running the actual Python regex.
/// </summary>
public class OsuVersionTests
{
    [Fact]
    public void From_WithRevisionAndStream_ParsesAllParts()
    {
        var version = OsuVersion.From("b20231231.1cuttingedge");

        Assert.Equal(new DateOnly(2023, 12, 31), version.Date);
        Assert.Equal(1, version.Revision);
        Assert.Equal(OsuStream.CuttingEdge, version.Stream);
    }

    [Fact]
    public void From_DateOnly_DefaultsToStableStreamAndNullRevision()
    {
        var version = OsuVersion.From("b20231231");

        Assert.Equal(new DateOnly(2023, 12, 31), version.Date);
        Assert.Null(version.Revision);
        Assert.Equal(OsuStream.Stable, version.Stream);
    }

    [Fact]
    public void From_TourneyStream_Recognized()
    {
        var version = OsuVersion.From("b20200201.2tourney");

        Assert.Equal(2, version.Revision);
        Assert.Equal(OsuStream.Tourney, version.Stream);
    }

    [Fact]
    public void From_StreamWithoutRevision_Recognized()
    {
        var version = OsuVersion.From("b20231231beta");

        Assert.Null(version.Revision);
        Assert.Equal(OsuStream.Beta, version.Stream);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("b2020")]
    [InlineData("b20231231.")]
    public void From_Malformed_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => OsuVersion.From(input));
    }
}
