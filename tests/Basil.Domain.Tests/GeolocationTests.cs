using System.Net;
using Basil.Domain.Login;

namespace Basil.Domain.Tests;

/// <summary>
///     Covers the resolved client-IP reading from proxy headers. A session's country comes from the
///     stored user record, never from headers.
/// </summary>
public class GeolocationTests
{
	[Fact]
	public void PhraseIpAddress_CfConnectingIpHeader_TakesPriority()
	{
		var headers = new Dictionary<string, string> { ["CF-Connecting-IP"] = "1.2.3.4", ["X-Real-IP"] = "9.9.9.9" };

		Assert.Equal(IPAddress.Parse("1.2.3.4"), Geolocation.PhraseIpAddress(headers));
	}

	[Fact]
	public void PhraseIpAddress_NoCloudflare_SingleForwardedFor_UsesXRealIp()
	{
		var headers = new Dictionary<string, string> { ["X-Forwarded-For"] = "5.6.7.8", ["X-Real-IP"] = "9.9.9.9" };

		Assert.Equal(IPAddress.Parse("9.9.9.9"), Geolocation.PhraseIpAddress(headers));
	}

	[Fact]
	public void PhraseIpAddress_NoCloudflare_MultipleForwardedFor_UsesFirstForwardedForEntry()
	{
		var headers = new Dictionary<string, string> { ["X-Forwarded-For"] = "5.6.7.8, 10.0.0.1" };

		Assert.Equal(IPAddress.Parse("5.6.7.8"), Geolocation.PhraseIpAddress(headers));
	}
}