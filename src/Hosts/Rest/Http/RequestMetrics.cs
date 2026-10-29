using System.Diagnostics;
using Microsoft.Extensions.Options;
using Basil.Host.Shared.Diagnostics;

namespace Basil.Host.Rest.Http;

/// <summary>Measures every request of every host on the shared meter, labelled with a fixed set of host labels.</summary>
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
