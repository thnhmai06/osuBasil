using Basil.Domain.Users;

namespace Basil.Domain.Tests.Users;

/// <summary>A username follows osu!'s registration rules; the check answers without throwing.</summary>
public sealed class UserNameTests
{
	[Theory]
	[InlineData("abc")]
	[InlineData("Player One")]
	[InlineData("a_b-c[d]")]
	[InlineData("123456789012345")]
	[InlineData("12a")]
	public void IsValidName_AcceptableName_ReturnsTrue(string name)
	{
		Assert.True(UserData.IsValidName(name));
	}

	[Theory]
	[InlineData("")]
	[InlineData("ab")]
	[InlineData("1234567890123456")]
	[InlineData(" abc")]
	[InlineData("abc ")]
	[InlineData("a  b")]
	[InlineData("12345")]
	[InlineData("a@b")]
	public void IsValidName_BreaksARule_ReturnsFalse(string name)
	{
		Assert.False(UserData.IsValidName(name));
	}

	[Fact]
	public void Name_SetterAndCheck_Agree()
	{
		Assert.Throws<ArgumentException>(() => new UserData { Name = "ab" });
	}
}
