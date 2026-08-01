namespace Basil.Web.Middleware;

/// <summary>
///     Logs every unhandled exception across all host groups at Error level, then rethrows.
///     Behavior-neutral (the exception propagates exactly as it did with no middleware here), so
///     <see cref="EnvelopeMiddleware" />'s response contract is never bypassed. This is the single
///     insertion point covering unhandled faults on every host group, feeding `errors_latest.log`.
/// </summary>
/// <remarks>
///     A client dropping the connection mid-response (bancho long-poll clients disconnect constantly,
///     e.g. game exit, network hiccup) surfaces here as an <see cref="OperationCanceledException" />
///     from writing to the aborted response stream. That is expected traffic noise, not a bug, so it
///     is swallowed at Debug instead of logged as Error and rethrown.
/// </remarks>
public sealed class ExceptionLoggingMiddleware(RequestDelegate next, ILogger<ExceptionLoggingMiddleware> logger)
{
	/// <summary>Runs the next middleware, logging any unhandled exception at Error level and rethrowing it.</summary>
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
			throw;
		}
	}
}