using System.Net;
using Microsoft.Extensions.Options;

namespace Basil.Host;

/// <summary>Removes proxy-provided client address and country headers from untrusted peers.</summary>
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

	/// <summary>Creates the middleware.</summary>
	/// <param name="next">The next step of the pipeline.</param>
	/// <param name="options">The server settings that name the trusted proxies.</param>
	/// <exception cref="FormatException">A trusted proxy is not an IP address or a CIDR range.</exception>
	public TrustedProxiesMiddleware(RequestDelegate next, IOptions<ServerOptions> options)
	{
		_next = next;
		_trusted = [.. options.Value.TrustedProxies.Select(ParseNetwork)];
	}

	/// <summary>Processes a request after deciding trust from its socket peer.</summary>
	/// <param name="context">The current HTTP request.</param>
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

/// <summary>Registers middleware that restricts forwarding headers to trusted proxy connections.</summary>
public static class TrustedProxiesMiddlewareExtensions
{
	/// <summary>Removes forwarding headers when the socket peer is not trusted.</summary>
	/// <param name="app">The application pipeline.</param>
	/// <returns>The application pipeline.</returns>
	public static IApplicationBuilder UseTrustedProxies(this IApplicationBuilder app)
	{
		return app.UseMiddleware<TrustedProxiesMiddleware>();
	}
}
