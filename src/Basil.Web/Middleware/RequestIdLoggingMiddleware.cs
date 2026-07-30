using System.Net;
using Basil.Domain.Login;
using Serilog.Context;

namespace Basil.Web.Middleware;

/// <summary>
///     Pushes the current request's <see cref="HttpContext.TraceIdentifier" /> and resolved client
///     IP onto Serilog's ambient LogContext for the entire downstream pipeline — every log line
///     emitted while handling this request, not just Serilog.AspNetCore's own per-request summary
///     line, carries it. Registered first in Program.cs's pipeline (before UseSerilogRequestLogging)
///     so the pushed scope also wraps that summary line's own emission.
/// </summary>
public sealed class RequestIdLoggingMiddleware(RequestDelegate next)
{
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