using System.Net;
using Basil.Domain.Users;
using Basil.Host.Bancho.Support;
using Microsoft.AspNetCore.Http;

namespace Basil.Host.Bancho.Tests.Support;

/// <summary>The caller's address and country are read in the order bancho.py uses.</summary>
public sealed class ClientAddressTests
{
	private static DefaultHttpContext Request(params (string Name, string Value)[] headers)
	{
		var context = new DefaultHttpContext();
		context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
		foreach (var (name, value) in headers) context.Request.Headers[name] = value;
		return context;
	}

	[Fact]
	public void Resolve_WithoutHeaders_IsTheSocketAddress()
	{
		Assert.Equal(IPAddress.Parse("10.0.0.1"), ClientAddress.Resolve(Request()));
	}

	[Fact]
	public void Resolve_CloudflareHeader_WinsOverTheOthers()
	{
		var context = Request(("CF-Connecting-IP", "1.1.1.1"), ("X-Forwarded-For", "2.2.2.2"),
			("X-Real-IP", "3.3.3.3"));

		Assert.Equal(IPAddress.Parse("1.1.1.1"), ClientAddress.Resolve(context));
	}

	[Fact]
	public void Resolve_ForwardedFor_UsesTheFirstEntry_BeforeRealIp()
	{
		var context = Request(("X-Forwarded-For", "2.2.2.2, 9.9.9.9"), ("X-Real-IP", "3.3.3.3"));

		Assert.Equal(IPAddress.Parse("2.2.2.2"), ClientAddress.Resolve(context));
	}

	[Fact]
	public void Resolve_RealIp_IsUsedWhenNothingElseIsGiven()
	{
		Assert.Equal(IPAddress.Parse("3.3.3.3"), ClientAddress.Resolve(Request(("X-Real-IP", "3.3.3.3"))));
	}

	[Fact]
	public void Resolve_GarbageHeader_FallsThroughToTheNextSource()
	{
		var context = Request(("CF-Connecting-IP", "not-an-ip"), ("X-Real-IP", "3.3.3.3"));

		Assert.Equal(IPAddress.Parse("3.3.3.3"), ClientAddress.Resolve(context));
	}

	[Fact]
	public void ResolveCountry_CloudflareHeader_WinsOverCountryCode()
	{
		var context = Request(("CF-IPCountry", "VN"), ("X-Country-Code", "US"));

		Assert.Equal(Country.Vn, ClientAddress.ResolveCountry(context));
	}

	[Fact]
	public void ResolveCountry_CountryCodeHeader_IsUsedWithoutCloudflare()
	{
		Assert.Equal(Country.Us, ClientAddress.ResolveCountry(Request(("X-Country-Code", "us"))));
	}

	[Fact]
	public void ResolveCountry_NoHeader_IsUnknown()
	{
		Assert.Null(ClientAddress.ResolveCountry(Request()));
	}
}