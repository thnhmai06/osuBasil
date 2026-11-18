using Basil.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Basil.Logging;

public static class TelemetrySetup
{
	public static bool Add(WebApplicationBuilder builder)
	{
		var options = builder.Configuration.GetSection("OpenTelemetry").Get<TelemetryOptions>() ??
		              new TelemetryOptions();
		builder.Services.AddOptions<TelemetryOptions>().Bind(builder.Configuration.GetSection("OpenTelemetry"));
		if (!Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out var endpoint) ||
		    !(options.Metrics || options.Traces))
			return false;

		var telemetry = builder.Services.AddOpenTelemetry().ConfigureResource(resource =>
			resource.AddService(builder.Configuration["Basil:Server:Name"] ?? "Basil"));
		if (options.Metrics)
			telemetry.WithMetrics(metrics => metrics.AddMeter("Basil.*").AddAspNetCoreInstrumentation()
				.AddRuntimeInstrumentation().AddOtlpExporter(exporter => exporter.Endpoint = endpoint));
		if (options.Traces)
			telemetry.WithTracing(tracing => tracing.AddAspNetCoreInstrumentation()
				.AddOtlpExporter(exporter => exporter.Endpoint = endpoint));
		return true;
	}
}