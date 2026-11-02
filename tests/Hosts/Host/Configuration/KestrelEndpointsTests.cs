using System.Net;
using Basil.Host.Configuration;
using Microsoft.Extensions.Configuration;

namespace Basil.Host.Tests.Configuration;

/// <summary>What the server reads from the standard <c>Kestrel:Endpoints</c> settings.</summary>
public sealed class KestrelEndpointsTests
{
	private static IConfiguration Endpoints(params string[] urls)
	{
		return new ConfigurationBuilder().AddInMemoryCollection(urls.Select((url, index) =>
			KeyValuePair.Create($"Kestrel:Endpoints:E{index}:Url", (string?)url))).Build();
	}

	[Fact]
	public void AnHttpsEndpointMeansTheServerServesTls()
	{
		Assert.True(KestrelSetup.UsesHttps(Endpoints("http://127.0.0.1:8080", "https://0.0.0.0:443")));
	}

	[Fact]
	public void OnlyHttpEndpointsMeanPlainHttpBehindAProxy()
	{
		Assert.False(KestrelSetup.UsesHttps(Endpoints("http://127.0.0.1:8080")));
	}

	[Fact]
	public void NoEndpointsMeansNoTls()
	{
		Assert.False(KestrelSetup.UsesHttps(Endpoints()));
	}

	[Fact]
	public void TheBindingsAreTheAddressesAndPortsOfTheEndpoints()
	{
		var bindings = KestrelSetup.Bindings(Endpoints("https://0.0.0.0:443", "http://[::1]:8080", "http://*:9000"));

		Assert.Equal(
			[new IPEndPoint(IPAddress.Any, 443), new IPEndPoint(IPAddress.IPv6Loopback, 8080),
				new IPEndPoint(IPAddress.Any, 9000)], bindings);
	}

	[Fact]
	public void AFreePortIsNotABinding()
	{
		Assert.Empty(KestrelSetup.Bindings(Endpoints("http://127.0.0.1:0")));
	}
}
