using System.Text.Json;
using Basil.Application.Formats;
using Basil.Web.OpenApi;

namespace Basil.Web.Middleware;

/// <summary>
///     Logs every unhandled exception across all host groups at Error level. On the <c>api.</c>
///     host, an exception that hasn't started writing a response yet also gets mapped to a
///     500 envelope instead of propagating to a bare, unenveloped response; every other host
///     group keeps the previous behavior of rethrowing unchanged. This is the single insertion
///     point covering unhandled faults on every host group, feeding `errors_latest.log`.
/// </summary>
/// <remarks>
///     A client dropping the connection mid-response surfaces here as an
///     <see cref="OperationCanceledException" /> from writing to the aborted response stream. Bancho
///     long-poll clients disconnect constantly, e.g., on game exit or network hiccup. That's
///     expected traffic noise, not a bug, so it's logged at Debug and swallowed, not logged as Error
///     and rethrown.
/// </remarks>
public sealed class ExceptionLoggingMiddleware(RequestDelegate next, ILogger<ExceptionLoggingMiddleware> logger)
{
	/// <summary>
	///     Runs the next middleware, logging any unhandled exception at Error level. On the
	///     <c>api.</c> host, writes a 500 envelope response instead of letting the exception
	///     propagate to a bare response, provided nothing has been written yet.
	/// </summary>
	/// <remarks>
	///     When the client aborts the request (the request's cancellation token fires), the abort is
	///     logged at Debug and is not rethrown.
	/// </remarks>
	/// <param name="context">The HTTP context whose request is being processed.</param>
	public async Task InvokeAsync(HttpContext context)
	{
		try
		{
			await next(context);
		}
		catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
		{
			logger.LogDebug("Request aborted by client: {Method} {Path}", context.Request.Method, context.Request.Path);
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Unhandled exception on {Method} {Path}",
				context.Request.Method, context.Request.Path);

			// Only the api. host has an envelope contract to uphold; every other host group (bancho,
			// osu-web, beatmap-assets, avatar) keeps its previous bare-response behavior unchanged.
			// A response that already started writing can't be retroactively wrapped, so it still
			// just propagates as before.
			if (context.Response.HasStarted ||
			    !context.Request.Host.Host.StartsWith("api.", StringComparison.OrdinalIgnoreCase))
				throw;

			context.Response.Clear();
			context.Response.StatusCode = StatusCodes.Status500InternalServerError;
			context.Response.ContentType = "application/json; charset=utf-8";
			var envelope = EnvelopeBuilder.Build(context.Response.StatusCode, context.Request.Method, null,
				BasilJsonOptions.Instance);
			await context.Response.WriteAsync(
				JsonSerializer.Serialize(envelope, BasilJsonOptions.Instance), context.RequestAborted);
		}
	}
}