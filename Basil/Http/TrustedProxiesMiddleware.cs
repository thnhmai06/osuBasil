using System.Net;
using Microsoft.Extensions.Options;

namespace Basil.Http;

internal sealed class TrustedProxiesMiddleware
{
	private static readonly string[] ForwardingHeaders =
	[
		"CF-Connecting-IP",
		"X-Forwarded-For",
		"X-Real-IP",
		"CF-IPCountry",
		"X-Country-Code"
	];

	private readonly RequestDelegate _next;
	private readonly IPNetwork[] _trusted;

	public TrustedProxiesMiddleware(RequestDelegate next, IOptions<ServerOptions> options)
	{
		_next = next;
		_trusted = [.. options.Value.TrustedProxies.Select(ParseNetwork)];
	}

	public Task InvokeAsync(HttpContext context)
	{
		// A dual-stack socket reports an IPv4 peer as an IPv4-mapped IPv6 address.
		var remoteIp = context.Connection.RemoteIpAddress is { IsIPv4MappedToIPv6: true } mapped
			? mapped.MapToIPv4()
			: context.Connection.RemoteIpAddress;
		if (remoteIp is null || !_trusted.Any(network => network.Contains(remoteIp)))
			foreach (var header in ForwardingHeaders)
				context.Request.Headers.Remove(header);

		return _next(context);
	}

	private static IPNetwork ParseNetwork(string proxy)
	{
		if (proxy.Contains('/')) return IPNetwork.Parse(proxy);

		var prefixLength = proxy.Contains(':') ? 128 : 32;
		return IPNetwork.Parse($"{proxy}/{prefixLength}");
	}
}

public static class TrustedProxiesMiddlewareExtensions
{
	public static IApplicationBuilder UseTrustedProxies(this IApplicationBuilder app)
	{
		return app.UseMiddleware<TrustedProxiesMiddleware>();
	}
}