using System.Diagnostics;
using Basil.Application.Diagnostics.Services;
using Basil.Host.Rest.Diagnostics;
using Microsoft.Extensions.Options;

namespace Basil.Host.Rest.Http;

internal sealed class RequestMetricsMiddleware(RequestDelegate next, IOptions<RestOptions> options)
{
	private readonly string _domain = options.Value.Domain;

	public async Task InvokeAsync(HttpContext context)
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			await next(context);
		}
		finally
		{
			var label = HostLabels.Resolve(context.Request.Host.Host, _domain);
			RestMeter.HttpDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
				new KeyValuePair<string, object?>(DiagnosticsMetrics.HostTag, label),
				new KeyValuePair<string, object?>(DiagnosticsMetrics.StatusTag,
					$"{context.Response.StatusCode / 100}xx"));
		}
	}
}

public static class RequestMetrics
{
	public static IApplicationBuilder UseRequestMetrics(this IApplicationBuilder app)
	{
		return app.UseMiddleware<RequestMetricsMiddleware>();
	}
}