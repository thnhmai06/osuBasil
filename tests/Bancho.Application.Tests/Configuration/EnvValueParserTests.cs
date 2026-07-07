using Bancho.Application.Configuration;

namespace Bancho.Application.Tests.Configuration;

/// <summary>
///     Ports the exact parsing behavior of bancho.py's app/settings_utils.py
///     (read_bool, read_list) so env-derived config parses identically.
/// </summary>
public class EnvValueParserTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("Yes", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("", false)]
    [InlineData("anything-else", false)]
    public void ReadBool_MatchesPythonSemantics(string value, bool expected)
    {
        Assert.Equal(expected, EnvValueParser.ReadBool(value));
    }

    [Fact]
    public void ReadList_SplitsOnComma_AndTrimsEachEntry()
    {
        var result = EnvValueParser.ReadList(" foo , bar ,baz");

        Assert.Equal(["foo", "bar", "baz"], result);
    }

    [Fact]
    public void ReadList_SingleValue_WithNoComma_ReturnsSingleElementList()
    {
        var result = EnvValueParser.ReadList("solo");

        Assert.Equal(["solo"], result);
    }

    [Fact]
    public void ReadList_TrailingComma_PreservesTrailingEmptyEntry()
    {
        // matches Python's `value.split(",")` which keeps a trailing empty string;
        // bancho.py does not filter these out.
        var result = EnvValueParser.ReadList("a,b,");

        Assert.Equal(["a", "b", ""], result);
    }
}