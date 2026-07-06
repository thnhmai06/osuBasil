namespace Bancho.Domain.Tests;

/// <summary>Ported from app/api/domains/cho.py's parse_adapters_string.</summary>
public class AdaptersStringParserTests
{
    [Fact]
    public void Parse_WineSentinel_ReturnsWineTrue()
    {
        var (adapters, runningUnderWine) = AdaptersStringParser.Parse("runningunderwine");

        Assert.Equal(["runningunderwine"], adapters);
        Assert.True(runningUnderWine);
    }

    [Fact]
    public void Parse_NormalAdapterList_SplitsOnDot()
    {
        var (adapters, runningUnderWine) = AdaptersStringParser.Parse("00:11:22.33:44:55.");

        Assert.Equal(["00:11:22", "33:44:55"], adapters);
        Assert.False(runningUnderWine);
    }

    [Fact]
    public void Parse_MissingTrailingDelimiter_Throws()
    {
        Assert.Throws<FormatException>(() => AdaptersStringParser.Parse("00:11:22"));
    }
}
