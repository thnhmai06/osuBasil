using System.Diagnostics;
using Basil.Host.Shared.Diagnostics;

namespace Basil.Host.Rest.Http;

/// <summary>Measures every request of every host on the shared meter, labelled with the host name's first label.</summary>
internal sealed class RequestMetricsMiddleware(RequestDelegate next)
{
	public async Task InvokeAsync(HttpContext context)
	{
		var started = Stopwatch.GetTimestamp();
		try
		{
			await next(context);
		}
		finally
		{
			var host = context.Request.Host.Host;
			var label = host.Contains('.') ? host[..host.IndexOf('.')] : host;
			BasilMeter.HttpDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
				new KeyValuePair<string, object?>("host", label),
				new KeyValuePair<string, object?>("status", $"{context.Response.StatusCode / 100}xx"));
		}
	}
}

/// <summary>Adds the measuring of requests to the pipeline.</summary>
public static class RequestMetrics
{
	/// <summary>Measures the duration of every request; add it before the hosts' own steps.</summary>
	/// <param name="app">The application pipeline.</param>
	/// <returns><paramref name="app" />, for chaining.</returns>
	public static IApplicationBuilder UseRequestMetrics(this IApplicationBuilder app)
	{
		return app.UseMiddleware<RequestMetricsMiddleware>();
	}
}
