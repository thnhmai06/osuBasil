using Basil.Host.Shared.Http;
using Microsoft.Extensions.Options;

namespace Basil.Http;

internal sealed class TrustedProxiesMiddleware(RequestDelegate next, IOptions<ServerOptions> options)
{
	private static bool IsForwardingHeader(string name)
	{
		return name.StartsWith("CF-", StringComparison.OrdinalIgnoreCase) ||
		       name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) ||
		       name.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase) ||
		       name.Equals("X-Country-Code", StringComparison.OrdinalIgnoreCase);
	}

	private readonly TrustedProxyList _trusted = new(options.Value.TrustedProxies);

	public Task InvokeAsync(HttpContext context)
	{
		if (!_trusted.Contains(context.Connection.RemoteIpAddress))
		{
			foreach (var header in context.Request.Headers.Keys.Where(IsForwardingHeader).ToList())
				context.Request.Headers.Remove(header);
		}
		else
		{
			// The proxy ends TLS, so the scheme of every absolute URL Basil builds is the one the client used.
			var scheme = context.Request.Headers["X-Forwarded-Proto"].ToString();
			if (scheme is "http" or "https") context.Request.Scheme = scheme;
		}

		return next(context);
	}
}

public static class TrustedProxiesMiddlewareExtensions
{
	public static IApplicationBuilder UseTrustedProxies(this IApplicationBuilder app)
	{
		return app.UseMiddleware<TrustedProxiesMiddleware>();
	}
}