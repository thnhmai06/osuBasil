namespace Basil.Web.Middleware;

/// <summary>
///     Logs every unhandled exception across all host groups at Error level, then rethrows —
///     behavior-neutral (the exception propagates exactly as it did with no middleware here), so
///     <see cref="EnvelopeMiddleware" />'s response contract is never bypassed. This is the single
///     insertion point covering unhandled faults on every host group, feeding `errors_latest.log`.
///     A client dropping the connection mid-response (bancho long-poll clients disconnect constantly —
///     game exit, network hiccup) surfaces here as an <see cref="OperationCanceledException" /> from
///     writing to the aborted response stream — that's expected traffic noise, not a bug, so it's
///     swallowed at Debug instead of logged as Error and rethrown.
/// </summary>
public sealed class ExceptionLoggingMiddleware(RequestDelegate next, ILogger<ExceptionLoggingMiddleware> logger)
{
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