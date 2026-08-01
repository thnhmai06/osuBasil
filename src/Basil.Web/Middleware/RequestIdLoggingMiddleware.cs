using System.Net;
using Basil.Domain.Login;
using Serilog.Context;

namespace Basil.Web.Middleware;

/// <summary>
///     Pushes the current request's <see cref="HttpContext.TraceIdentifier" /> and resolved client
///     IP onto Serilog's ambient LogContext for the entire downstream pipeline. Every log line
///     emitted while handling this request, not just Serilog.AspNetCore's own per-request summary
///     line, carries it. Registered first in Program.cs's pipeline (before UseSerilogRequestLogging)
///     so the pushed scope also wraps that summary line's own emission.
/// </summary>
public sealed class RequestIdLoggingMiddleware(RequestDelegate next)
{
	/// <summary>Pushes the request's trace identifier and resolved client IP onto Serilog's LogContext for the request.</summary>
	/// <remarks>
	///     When the request carries neither a <c>CF-Connecting-IP</c> nor an <c>X-Forwarded-For</c>
	///     header, the remote IP (falling back to loopback) is written into both headers before the IP
	///     phrase is resolved.
	/// </remarks>
	/// <param name="context">The HTTP context whose request headers and connection IP are examined.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		var headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
		if (!headers.ContainsKey("CF-Connecting-IP") && !headers.ContainsKey("X-Forwarded-For"))
		{
			var remoteIp = (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString();
			headers["X-Forwarded-For"] = remoteIp;
			headers["X-Real-IP"] = remoteIp;
		}

		using (LogContext.PushProperty("RequestId", context.TraceIdentifier))
		using (LogContext.PushProperty("RemoteIp", Geolocation.PhraseIpAddress(headers).ToString()))
		{
			await next(context);
		}
	}
}