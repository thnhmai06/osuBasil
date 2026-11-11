using System.Diagnostics;
using Basil.Application.Diagnostics.Services;
using Microsoft.Extensions.Options;

namespace Basil.Host.Rest.Http;

/// <summary>Measures every request of every host on the shared meter, labelled with a fixed set of host labels.</summary>
internal sealed class RequestMetricsMiddleware(RequestDelegate next, IOptions<RestOptions> options)
{
	private readonly string _domain = options.Value.Domain;

	/// <summary>Counts the request and times how long it took to answer.</summary>
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