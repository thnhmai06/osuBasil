using Basil.Host.Cli.Dashboard.Shell;

namespace Basil.Host.Cli.Tests.Dashboard;

[Collection(HostedTerminalCollection.Name)]
public sealed class TabSpreadTests
{
	// Labels of 4 and 6 columns take 10, and one separator per tab takes 2, so the free space is width - 12.
	[Theory]
	[InlineData(28, 4, 4)]
	[InlineData(30, 4, 5)]
	[InlineData(16, 1, 1)]
	public void Padding_SplitsTheFreeSpaceEquallyPerTab_TheOddColumnAfterTheLabel(int width, int before, int after)
	{
		Assert.Equal((before, after), TabSpread.Padding(width, [4, 6]));
	}

	[Theory]
	[InlineData(15)]
	[InlineData(12)]
	[InlineData(5)]
	public void Padding_WhenEachTabWouldGetFewerThanTwoColumns_IsNoneBeforeAndOneAfter(int width)
	{
		Assert.Equal((0, 1), TabSpread.Padding(width, [4, 6]));
	}
}
