using Basil.Domain.Users;

namespace Basil.Domain.Tests.Users;

public sealed class UserNameTests
{
	[Theory]
	[InlineData("abc")]
	[InlineData("Player One")]
	[InlineData("a_b-c[d]")]
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
	[InlineData("123456789012345")]
	[InlineData("a@b")]
	public void InvalidName_ReturnsFalse(string name)
	{
		Assert.False(UserData.IsValidName(name));
	}

	[Fact]
	public void SetterAndCheck_Agree()
	{
		Assert.Throws<ArgumentException>(() => new UserData { Name = "ab" });
	}
}