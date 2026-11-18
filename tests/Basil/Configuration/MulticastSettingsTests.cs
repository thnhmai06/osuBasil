using Basil.Configuration;
using Basil.Host.Irc.Transport;

namespace Basil.Tests.Configuration;

public sealed class MulticastSettingsTests
{
	[Fact]
	public void Advertisement_OffByDefault()
	{
		Assert.False(new ServerOptions().Multicast.Enabled);
	}

	[Fact]
	public void BothFamilies_OnByDefault()
	{
		using var service = LanAdvertiser.NewService(new ServerOptions.MulticastSettings());

		Assert.True(service.UseIpv4);
		Assert.True(service.UseIpv6);
	}

	[Fact]
	public void Ipv4False_SwitchesOff()
	{
		using var service = LanAdvertiser.NewService(new ServerOptions.MulticastSettings { UseIPv4 = false });

		Assert.False(service.UseIpv4);
		Assert.True(service.UseIpv6);
	}

	[Fact]
	public void Ipv6False_SwitchesOff()
	{
		using var service = LanAdvertiser.NewService(new ServerOptions.MulticastSettings { UseIPv6 = false });

		Assert.True(service.UseIpv4);
		Assert.False(service.UseIpv6);
	}

	[Fact]
	public void Names_IncludeIrcServer()
	{
		var irc = new IrcOptions { Domain = "basil.local" };

		Assert.Contains(irc.ServerName, LanAdvertiser.Names(irc.Domain));
	}
}