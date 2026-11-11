using Basil.Application.Chat.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Chat.Tests.Objects;

public sealed class GeneralChannelChangeTests
{
	[Fact]
	public void IsValid_NothingSet_IsTrue()
	{
		Assert.True(new GeneralChannelChange().IsValid);
	}

	[Fact]
	public void IsValid_EveryFieldValid_IsTrue()
	{
		Assert.True(new GeneralChannelChange("topic", UserPrivileges.Player, UserPrivileges.Moderator, true, false)
			.IsValid);
	}

	[Fact]
	public void IsValid_TopicWithNul_IsFalse()
	{
		Assert.False(new GeneralChannelChange("a\0b").IsValid);
	}

	[Fact]
	public void IsValid_ReadPrivilegesWithAnUndefinedBit_IsFalse()
	{
		Assert.False(new GeneralChannelChange(ReadPrivileges: (UserPrivileges)64).IsValid);
	}

	[Fact]
	public void IsValid_WritePrivilegesWithAnUndefinedBit_IsFalse()
	{
		Assert.False(new GeneralChannelChange(WritePrivileges: (UserPrivileges)128).IsValid);
	}
}