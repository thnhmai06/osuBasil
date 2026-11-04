namespace Basil.Tests.Configuration;

/// <summary>The multicast DNS answers are off by default.</summary>
public sealed class MulticastSettingsTests
{
	[Fact]
	public void TheAdvertisementIsOffByDefault()
	{
		Assert.False(new ServerOptions().Multicast.Enabled);
	}
}
