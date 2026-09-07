using System.Net;
using Basil.Domain.Login;
using Serilog.Context;

namespace Basil.Server.Shared.Http.Middleware;

/// <summary>
///     Pushes the current request's <see cref="HttpContext.TraceIdentifier" /> and resolved client IP
///     as ambient log properties for the entire downstream pipeline, so every log line emitted while
///     handling this request carries them.
/// </summary>
public sealed class RequestIdLoggingMiddleware(RequestDelegate next)
{
	/// <summary>Pushes the request's trace identifier and resolved client IP as ambient log properties for the request.</summary>
	/// <remarks>
	///     When the request carries neither a <c>CF-Connecting-IP</c> nor an <c>X-Forwarded-For</c>
	///     header, the remote IP (falling back to loopback) is written into both headers before the IP
	///     phrase is resolved. Only the three headers <see cref="Geolocation.PhraseIpAddress" /> reads
	///     are copied out of the request — not the full header collection, which every request on every
	///     host group previously paid to materialize.
	/// </remarks>
	/// <param name="context">The HTTP context whose request headers and connection IP are examined.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		var headers = BuildIpHeaders(context);

		using (LogContext.PushProperty("RequestId", context.TraceIdentifier))
		using (LogContext.PushProperty("RemoteIp", Geolocation.PhraseIpAddress(headers).ToString()))
		{
			await next(context);
		}
	}

	/// <summary>
	///     Builds the small header set <see cref="Geolocation.PhraseIpAddress" /> reads, synthesizing
	///     <c>X-Forwarded-For</c>/<c>X-Real-IP</c> from the direct connection when neither it nor
	///     <c>CF-Connecting-IP</c> was sent.
	/// </summary>
	/// <param name="context">The HTTP context whose request headers and connection IP are examined.</param>
	/// <returns>A small header dictionary with only the keys <see cref="Geolocation.PhraseIpAddress" /> reads.</returns>
	internal static Dictionary<string, string> BuildIpHeaders(HttpContext context)
	{
		var requestHeaders = context.Request.Headers;
		var headers = new Dictionary<string, string>(3);

		var cfConnectingIp = requestHeaders["CF-Connecting-IP"];
		if (cfConnectingIp.Count > 0) headers["CF-Connecting-IP"] = cfConnectingIp.ToString();

		var forwardedFor = requestHeaders["X-Forwarded-For"];
		if (forwardedFor.Count > 0) headers["X-Forwarded-For"] = forwardedFor.ToString();

		var realIp = requestHeaders["X-Real-IP"];
		if (realIp.Count > 0) headers["X-Real-IP"] = realIp.ToString();

		if (!headers.ContainsKey("CF-Connecting-IP") && !headers.ContainsKey("X-Forwarded-For"))
		{
			var remoteIp = (context.Connection.RemoteIpAddress ?? IPAddress.Loopback).ToString();
			headers["X-Forwarded-For"] = remoteIp;
			headers["X-Real-IP"] = remoteIp;
		}

		return headers;
	}
}