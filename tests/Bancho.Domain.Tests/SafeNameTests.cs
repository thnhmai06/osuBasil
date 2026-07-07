using Bancho.Domain.Users;

namespace Bancho.Domain.Tests;

/// <summary>Ported from app/utils.py's make_safe_name — lowercases and replaces spaces with underscores.</summary>
public class SafeNameTests
{
    [Theory]
    [InlineData("cmyui", "cmyui")]
    [InlineData("Cool Guy", "cool_guy")]
    [InlineData("ALL CAPS NAME", "all_caps_name")]
    public void Make_LowercasesAndReplacesSpacesWithUnderscores(string name, string expected)
    {
        Assert.Equal(expected, SafeName.Make(name));
    }
}