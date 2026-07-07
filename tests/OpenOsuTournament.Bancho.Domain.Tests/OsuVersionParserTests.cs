using OpenOsuTournament.Bancho.Domain.Login;

namespace OpenOsuTournament.Bancho.Domain.Tests;

/// <summary>
///     Ported from app/constants/regexes.py's OSU_VERSION + app/api/domains/cho.py's
///     parse_osu_version_string. Reference behavior verified by running the actual Python regex.
/// </summary>
public class OsuVersionParserTests
{
    [Fact]
    public void Parse_WithRevisionAndStream_ParsesAllParts()
    {
        var version = OsuVersionParser.Parse("b20231231.1cuttingedge");

        Assert.NotNull(version);
        Assert.Equal(new DateOnly(2023, 12, 31), version!.Date);
        Assert.Equal(1, version.Revision);
        Assert.Equal(OsuStream.CuttingEdge, version.Stream);
    }

    [Fact]
    public void Parse_DateOnly_DefaultsToStableStreamAndNullRevision()
    {
        var version = OsuVersionParser.Parse("b20231231");

        Assert.NotNull(version);
        Assert.Equal(new DateOnly(2023, 12, 31), version!.Date);
        Assert.Null(version.Revision);
        Assert.Equal(OsuStream.Stable, version.Stream);
    }

    [Fact]
    public void Parse_TourneyStream_Recognized()
    {
        var version = OsuVersionParser.Parse("b20200201.2tourney");

        Assert.NotNull(version);
        Assert.Equal(2, version!.Revision);
        Assert.Equal(OsuStream.Tourney, version.Stream);
    }

    [Fact]
    public void Parse_StreamWithoutRevision_Recognized()
    {
        var version = OsuVersionParser.Parse("b20231231beta");

        Assert.NotNull(version);
        Assert.Null(version!.Revision);
        Assert.Equal(OsuStream.Beta, version.Stream);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("b2020")]
    [InlineData("b20231231.")]
    public void Parse_Malformed_ReturnsNull(string input)
    {
        Assert.Null(OsuVersionParser.Parse(input));
    }
}