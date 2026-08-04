using System.Diagnostics;
using Basil.Web.Routing.Api;

namespace Basil.Web.Middleware;

/// <summary>
///     Logs one Information line per completed request on the `api.` host, skipping its live SSE
///     channels.
/// </summary>
/// <remarks>
///     A stream that would push data indefinitely has no meaningful "completed" line — it would only
///     log when the client disconnects, with a misleadingly long duration — so every route whose path
///     carries a literal <c>live</c> segment (the same structural signature
///     <see cref="EnvelopeMiddleware" /> uses, via <see cref="LiveSseRoutes.IsSseRoute" />) is skipped.
///     Only the <c>basilapi</c> group is covered; every other host group (bancho/osu-web/beatmap-assets/
///     avatar) is left to the global <c>UseSerilogRequestLogging()</c> line.
/// </remarks>
public sealed class ApiRequestLoggingMiddleware(RequestDelegate next, ILogger<ApiRequestLoggingMiddleware> logger)
{
	/// <summary>Logs the completed api. host request, skipping live SSE channels.</summary>
	/// <param name="context">The HTTP context whose request is measured and logged.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		var endpoint = context.GetEndpoint();
		var groupName = endpoint?.Metadata.GetMetadata<IEndpointGroupNameMetadata>()?.EndpointGroupName;
		var isAlwaysSse = endpoint is RouteEndpoint { RoutePattern.RawText: { } raw } &&
		                  LiveSseRoutes.IsSseRoute(raw);
		if (groupName != "basilapi" || isAlwaysSse)
		{
			await next(context);
			return;
		}

		var stopwatch = Stopwatch.StartNew();
		try
		{
			await next(context);
			logger.LogInformation("API request completed: {Method} {Path} -> {StatusCode} in {ElapsedMs} ms",
				context.Request.Method, context.Request.Path, context.Response.StatusCode,
				stopwatch.ElapsedMilliseconds);
		}
		finally
		{
			stopwatch.Stop();
		}
	}
}