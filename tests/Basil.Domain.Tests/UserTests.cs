using Basil.Domain.Users;

namespace Basil.Domain.Tests;

public class UserTests
{
    [Theory]
    [InlineData("cmyui", "cmyui")]
    [InlineData("Cool Guy", "cool_guy")]
    [InlineData("ALL CAPS NAME", "all_caps_name")]
    public void MakeSafeName_LowercasesAndReplacesSpacesWithUnderscores(string name, string expected)
    {
        Assert.Equal(expected, User.MakeSafeName(name));
    }

    [Theory]
    [InlineData("abc")] // min length
    [InlineData("123456789012345")] // max length (15)
    [InlineData("cool_guy")]
    [InlineData("cool-guy")]
    [InlineData("[tag]player")]
    [InlineData("Cool Guy")]
    public void ValidateUsername_ValidNames_ReturnsTrue(string name)
    {
        Assert.True(User.ValidateUsername(name, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void ValidateUsername_TooShort_ReturnsFalse()
    {
        Assert.False(User.ValidateUsername("ab", out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateUsername_TooLong_ReturnsFalse()
    {
        Assert.False(User.ValidateUsername("1234567890123456", out var error)); // 16 chars
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateUsername_LeadingSpace_ReturnsFalse()
    {
        Assert.False(User.ValidateUsername(" cool guy", out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateUsername_TrailingSpace_ReturnsFalse()
    {
        Assert.False(User.ValidateUsername("cool guy ", out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateUsername_ConsecutiveSpaces_ReturnsFalse()
    {
        Assert.False(User.ValidateUsername("cool  guy", out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("Tuấn Anh")] // Vietnamese diacritics
    [InlineData("プレイヤー")] // Japanese
    [InlineData("player@home")] // disallowed symbol
    [InlineData("player!")]
    public void ValidateUsername_DisallowedCharacters_ReturnsFalse(string name)
    {
        Assert.False(User.ValidateUsername(name, out var error));
        Assert.NotNull(error);
    }
}
