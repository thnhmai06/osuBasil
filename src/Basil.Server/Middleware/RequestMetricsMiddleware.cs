using System.Diagnostics;
using Basil.Application.Diagnostics;

namespace Basil.Web.Middleware;

/// <summary>
///     Records total request duration to <see cref="BasilMetrics.RequestDurationMs" />, tagged by
///     Basil host group (<c>bancho</c>/<c>osuweb</c>/<c>beatmapassets</c>/<c>avatar</c>/<c>basilapi</c>/
///     <c>assets</c> — the same <c>.WithGroupName(...)</c> value every route already carries). Placed
///     first in the pipeline so the measured duration includes every other middleware's cost, not just
///     the endpoint handler's.
/// </summary>
/// <remarks>
///     Not applied to routes with a literal <c>live</c> path segment (SSE streams) — see
///     <see cref="ApiRequestLoggingMiddleware" />'s doc for why a connection held open indefinitely has
///     no meaningful "request duration".
/// </remarks>
public sealed class RequestMetricsMiddleware(RequestDelegate next)
{
	/// <summary>Times the request and records it by host group once the endpoint has resolved.</summary>
	/// <param name="context">The HTTP context whose request is measured.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		var isAlwaysSse = context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } raw } &&
		                  Routing.Api.LiveSseRoutes.IsSseRoute(raw);
		if (isAlwaysSse)
		{
			await next(context);
			return;
		}

		var startedAt = Stopwatch.GetTimestamp();
		try
		{
			await next(context);
		}
		finally
		{
			var groupName = context.GetEndpoint()?.Metadata.GetMetadata<IEndpointGroupNameMetadata>()
				?.EndpointGroupName ?? "unmatched";
			BasilMetrics.RequestDurationMs.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
				new KeyValuePair<string, object?>("host.group", groupName));
		}
	}
}
