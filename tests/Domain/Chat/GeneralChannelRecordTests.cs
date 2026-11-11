using Basil.Domain.Chat;
using Basil.Domain.Users;

namespace Basil.Domain.Tests.Chat;

public class GeneralChannelRecordTests
{
	[Fact]
	public void HashPrefixedName_Accepted()
	{
		Assert.Equal("#osu", new GeneralChannelRecord { Name = "#osu", Value = new GeneralChannelData() }.Name);
	}

	[Theory]
	[InlineData("")]
	[InlineData("osu")]
	[InlineData("#")]
	[InlineData("#staff room")]
	[InlineData("#staff,room")]
	[InlineData("#staff\0room")]
	public void InvalidName_Throws(string name)
	{
		Assert.Throws<ArgumentException>(() => new GeneralChannelRecord
			{ Name = name, Value = new GeneralChannelData() });
	}

	[Fact]
	public void NullTopic_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new GeneralChannelData { Topic = null! });
	}

	[Theory]
	[InlineData("#osu")]
	[InlineData("#a")]
	[InlineData("#staff-room_1")]
	public void IsValidName_HashPrefixedNameWithoutSeparators_IsTrue(string name)
	{
		Assert.True(GeneralChannelRecord.IsValidName(name));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("osu")]
	[InlineData("#")]
	[InlineData("#staff room")]
	[InlineData("#staff\troom")]
	[InlineData("#staff,room")]
	[InlineData("#staff\0room")]
	public void IsValidName_EachBrokenRule_IsFalse(string? name)
	{
		Assert.False(GeneralChannelRecord.IsValidName(name));
	}

	[Theory]
	[InlineData("")]
	[InlineData("Welcome to #osu")]
	public void IsValidTopic_TextWithoutNul_IsTrue(string topic)
	{
		Assert.True(GeneralChannelData.IsValidTopic(topic));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("\0")]
	[InlineData("topic\0")]
	public void IsValidTopic_NullOrTextWithNul_IsFalse(string? topic)
	{
		Assert.False(GeneralChannelData.IsValidTopic(topic));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(32)]
	[InlineData(63)]
	public void IsValidPrivileges_OnlyDefinedBits_IsTrue(int bits)
	{
		Assert.True(GeneralChannelData.IsValidPrivileges((UserPrivileges)bits));
	}

	[Theory]
	[InlineData(64)]
	[InlineData(128)]
	[InlineData(65)]
	public void IsValidPrivileges_AnUndefinedBit_IsFalse(int bits)
	{
		Assert.False(GeneralChannelData.IsValidPrivileges((UserPrivileges)bits));
	}
}