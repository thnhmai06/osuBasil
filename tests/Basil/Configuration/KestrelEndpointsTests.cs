using System.Net;
using Basil.Configuration;
using Microsoft.Extensions.Configuration;

namespace Basil.Tests.Configuration;

/// <summary>What the server reads from the standard <c>Kestrel:Endpoints</c> settings.</summary>
public sealed class KestrelEndpointsTests
{
	private static IConfiguration Endpoints(params string[] urls)
	{
		return new ConfigurationBuilder().AddInMemoryCollection(urls.Select((url, index) =>
			KeyValuePair.Create($"Kestrel:Endpoints:E{index}:Url", (string?)url))).Build();
	}

	[Fact]
	public void HttpsEndpoint_ServesTls()
	{
		Assert.True(KestrelSetup.UsesHttps(Endpoints("http://127.0.0.1:8080", "https://0.0.0.0:443")));
	}

	[Theory]
	[InlineData("basil", true)]
	[InlineData("my-own-password", false)]
	public void ShippedCertPassword_OnlyWithTls(string password, bool expected)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Kestrel:Endpoints:E0:Url"] = "https://0.0.0.0:443",
			["Kestrel:Certificates:Default:Password"] = password
		}).Build();

		Assert.Equal(expected, KestrelSetup.UsesPublishedCertificate(configuration));
	}

	[Fact]
	public void CertNoTls_NotIssue()
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Kestrel:Endpoints:E0:Url"] = "http://127.0.0.1:8080",
			["Kestrel:Certificates:Default:Password"] = "basil"
		}).Build();

		Assert.False(KestrelSetup.UsesPublishedCertificate(configuration));
	}

	[Fact]
	public void OnlyHttp_PlainBehindProxy()
	{
		Assert.False(KestrelSetup.UsesHttps(Endpoints("http://127.0.0.1:8080")));
	}

	[Fact]
	public void NoEndpoints_NoTls()
	{
		Assert.False(KestrelSetup.UsesHttps(Endpoints()));
	}

	[Fact]
	public void Bindings_AddressesAndPorts()
	{
		var bindings = KestrelSetup.Bindings(Endpoints("https://0.0.0.0:443", "http://[::1]:8080", "http://*:9000"));

		Assert.Equal(
		[
			new IPEndPoint(IPAddress.Any, 443), new IPEndPoint(IPAddress.IPv6Loopback, 8080),
			new IPEndPoint(IPAddress.Any, 9000)
		], bindings);
	}

	[Fact]
	public void FreePort_NotBinding()
	{
		Assert.Empty(KestrelSetup.Bindings(Endpoints("http://127.0.0.1:0")));
	}
}