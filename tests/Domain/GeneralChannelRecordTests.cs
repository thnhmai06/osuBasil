using Basil.Domain.Chat;

namespace Basil.Domain.Tests;

public class GeneralChannelRecordTests
{
	[Fact]
	public void Name_HashPrefixedName_IsAccepted()
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
	public void Name_InvalidChannelName_Throws(string name)
	{
		Assert.Throws<ArgumentException>(() => new GeneralChannelRecord { Name = name, Value = new GeneralChannelData() });
	}
}