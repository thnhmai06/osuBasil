namespace Basil.Web.Middleware;

/// <summary>
///     Logs every unhandled exception across all host groups at Error level, then rethrows —
///     behavior-neutral (the exception propagates exactly as it did with no middleware here), so
///     <see cref="EnvelopeMiddleware" />'s response contract is never bypassed. This is the single
///     insertion point covering unhandled faults on every host group, feeding `errors_latest.log`.
/// </summary>
public sealed class ExceptionLoggingMiddleware(RequestDelegate next, ILogger<ExceptionLoggingMiddleware> logger)
{
	public async Task InvokeAsync(HttpContext context)
	{
		try
		{
			await next(context);
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Unhandled exception on {Method} {Path}",
				context.Request.Method, context.Request.Path);
			throw;
		}
	}
}