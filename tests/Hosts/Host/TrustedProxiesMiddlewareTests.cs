using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Basil.Host.Tests;

public sealed class TrustedProxiesMiddlewareTests
{
	[Fact]
	public async Task InvokeAsync_UntrustedSocket_RemovesForwardingHeaders()
	{
		var context = new DefaultHttpContext();
		context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
		SetForwardingHeaders(context);

		await new TrustedProxiesMiddleware(_ => Task.CompletedTask, Options.Create(new ServerOptions()))
			.InvokeAsync(context);

		Assert.All(ForwardingHeaders, header => Assert.False(context.Request.Headers.ContainsKey(header)));
	}

	[Fact]
	public async Task InvokeAsync_TrustedSocket_RetainsForwardingHeaders()
	{
		var context = new DefaultHttpContext();
		context.Connection.RemoteIpAddress = IPAddress.Parse("10.23.4.5");
		SetForwardingHeaders(context);
		var options = Options.Create(new ServerOptions { TrustedProxies = ["10.0.0.0/8"] });

		await new TrustedProxiesMiddleware(_ => Task.CompletedTask, options).InvokeAsync(context);

		Assert.All(ForwardingHeaders, header => Assert.True(context.Request.Headers.ContainsKey(header)));
	}

	[Theory]
	[InlineData("127.0.0.1")]
	[InlineData("::1")]
	public async Task InvokeAsync_DefaultTrustedProxies_KeepHeadersFromTheLoopback(string peer)
	{
		var context = new DefaultHttpContext();
		context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
		SetForwardingHeaders(context);

		await new TrustedProxiesMiddleware(_ => Task.CompletedTask, Options.Create(new ServerOptions()))
			.InvokeAsync(context);

		Assert.All(ForwardingHeaders, header => Assert.True(context.Request.Headers.ContainsKey(header)));
	}

	[Fact]
	public async Task InvokeAsync_UnknownSocketAddress_RemovesForwardingHeaders()
	{
		var context = new DefaultHttpContext();
		SetForwardingHeaders(context);

		await new TrustedProxiesMiddleware(_ => Task.CompletedTask, Options.Create(new ServerOptions()))
			.InvokeAsync(context);

		Assert.All(ForwardingHeaders, header => Assert.False(context.Request.Headers.ContainsKey(header)));
	}

	[Fact]
	public async Task InvokeAsync_UntrustedSocket_KeepsOtherHeadersAndCallsTheNextMiddleware()
	{
		var context = new DefaultHttpContext();
		context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
		context.Request.Headers["User-Agent"] = "client";
		var called = false;

		await new TrustedProxiesMiddleware(_ =>
		{
			called = true;
			return Task.CompletedTask;
		}, Options.Create(new ServerOptions())).InvokeAsync(context);

		Assert.True(called);
		Assert.Equal("client", context.Request.Headers["User-Agent"]);
	}

	[Fact]
	public async Task InvokeAsync_TrustedIpv4PeerSeenAsAnIpv4MappedAddress_KeepsHeaders()
	{
		var context = new DefaultHttpContext();
		context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:127.0.0.1");
		SetForwardingHeaders(context);

		await new TrustedProxiesMiddleware(_ => Task.CompletedTask, Options.Create(new ServerOptions()))
			.InvokeAsync(context);

		Assert.All(ForwardingHeaders, header => Assert.True(context.Request.Headers.ContainsKey(header)));
	}

	[Fact]
	public void Constructor_WithAnInvalidTrustedProxy_FailsAtStartup()
	{
		var options = Options.Create(new ServerOptions { TrustedProxies = ["not an address"] });

		Assert.ThrowsAny<FormatException>(() => new TrustedProxiesMiddleware(_ => Task.CompletedTask, options));
	}

	private static readonly string[] ForwardingHeaders =
	[
		"CF-Connecting-IP",
		"X-Forwarded-For",
		"X-Real-IP",
		"CF-IPCountry",
		"X-Country-Code"
	];

	private static void SetForwardingHeaders(HttpContext context)
	{
		foreach (var header in ForwardingHeaders)
			context.Request.Headers[header] = "value";
	}
}
