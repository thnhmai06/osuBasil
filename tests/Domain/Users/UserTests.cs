using Basil.Domain.Users;

namespace Basil.Domain.Tests.Users;

public class UserTests
{
	[Theory]
	[InlineData("cmyui", "cmyui")]
	[InlineData("Cool Guy", "cool_guy")]
	[InlineData("ALL CAPS NAME", "all_caps_name")]
	public void NormalizedName_LowercasesAndReplacesSpacesWithUnderscores(string name, string expected)
	{
		Assert.Equal(expected, new UserData { Name = name }.NormalizedName);
	}

	[Theory]
	[InlineData("abc")]
	[InlineData("123456789abcdef")]
	[InlineData("cool_guy")]
	[InlineData("cool-guy")]
	[InlineData("[tag]player")]
	[InlineData("Cool Guy")]
	public void Name_ValidRegistrationName_IsAccepted(string name)
	{
		Assert.Equal(name, new UserData { Name = name }.Name);
	}

	[Theory]
	[InlineData("ab")]
	[InlineData("123456789")]
	[InlineData("123456789abcdefg")]
	[InlineData(" cool guy")]
	[InlineData("cool guy ")]
	[InlineData("cool  guy")]
	[InlineData("Tuấn Anh")]
	[InlineData("プレイヤー")]
	[InlineData("player@home")]
	[InlineData("player!")]
	public void Name_InvalidRegistrationName_Throws(string name)
	{
		Assert.Throws<ArgumentException>(() => new UserData { Name = name });
	}

	[Fact]
	public void DeletedAt_DeletedAccountCannotBeRestored()
	{
		var user = new UserData { Name = "player", DeletedAt = DateTimeOffset.UnixEpoch };

		Assert.Throws<InvalidOperationException>(() => user.DeletedAt = null);
		Assert.Equal(DateTimeOffset.UnixEpoch, user.DeletedAt);
	}

	[Fact]
	public void Name_TrailingNewline_Throws()
	{
		Assert.Throws<ArgumentException>(() => new UserData { Name = "abc\n" });
	}

	[Fact]
	public void Name_Null_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new UserData { Name = null! });
	}

	[Fact]
	public void Country_Undefined_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new UserData { Name = "player", Country = (Country)255 });
	}

	[Fact]
	public void Country_UndefinedCode_Throws()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new UserData { Name = "player", Country = default });
	}
}