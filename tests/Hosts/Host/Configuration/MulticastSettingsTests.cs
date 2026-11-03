using Basil.Host.Configuration;

namespace Basil.Host.Tests.Configuration;

/// <summary>The multicast DNS answers are off by default, and a group address or port the responder cannot use is refused.</summary>
public sealed class MulticastSettingsTests
{
	[Fact]
	public void TheAdvertisementIsOffByDefault()
	{
		Assert.False(new ServerOptions().Multicast.Enabled);
	}

	[Fact]
	public void TheStandardGroupsAndPortAreAccepted()
	{
		LanAdvertiser.Validate(new ServerOptions.MulticastSettings());
	}

	[Fact]
	public void AnEmptyFamilyIsAccepted()
	{
		LanAdvertiser.Validate(new ServerOptions.MulticastSettings { IPv4 = "", IPv6 = null });
	}

	[Fact]
	public void TheIpv6GroupIsComparedAsAnAddressNotAsText()
	{
		LanAdvertiser.Validate(new ServerOptions.MulticastSettings { IPv6 = "ff02:0:0:0:0:0:0:fb" });
	}

	[Theory]
	[InlineData("IPv4", "239.1.1.1")]
	[InlineData("IPv6", "FF02::1")]
	[InlineData("Port", "5454")]
	public void AnUnsupportedGroupOrPortIsRefusedInsteadOfIgnored(string setting, string value)
	{
		var multicast = new ServerOptions.MulticastSettings();
		switch (setting)
		{
			case "IPv4": multicast.IPv4 = value; break;
			case "IPv6": multicast.IPv6 = value; break;
			default: multicast.Port = int.Parse(value); break;
		}

		var exception = Assert.Throws<InvalidOperationException>(() => LanAdvertiser.Validate(multicast));

		Assert.Contains("Multicast:" + setting, exception.Message);
	}
}