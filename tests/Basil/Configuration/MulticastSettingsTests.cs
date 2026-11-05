using Basil.Configuration;

namespace Basil.Tests.Configuration;

/// <summary>The multicast DNS answers are off by default.</summary>
public sealed class MulticastSettingsTests
{
	[Fact]
	public void TheAdvertisementIsOffByDefault()
	{
		Assert.False(new ServerOptions().Multicast.Enabled);
	}

	[Fact]
	public void BothFamiliesAreOnByDefault()
	{
		using var service = LanAdvertiser.NewService(new ServerOptions.MulticastSettings());

		Assert.True(service.UseIpv4);
		Assert.True(service.UseIpv6);
	}

	[Fact]
	public void UseIPv4FalseSwitchesIpv4Off()
	{
		using var service = LanAdvertiser.NewService(new ServerOptions.MulticastSettings { UseIPv4 = false });

		Assert.False(service.UseIpv4);
		Assert.True(service.UseIpv6);
	}

	[Fact]
	public void UseIPv6FalseSwitchesIpv6Off()
	{
		using var service = LanAdvertiser.NewService(new ServerOptions.MulticastSettings { UseIPv6 = false });

		Assert.True(service.UseIpv4);
		Assert.False(service.UseIpv6);
	}
}
