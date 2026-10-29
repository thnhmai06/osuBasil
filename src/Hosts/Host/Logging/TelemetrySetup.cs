using Basil.Host.Shared.Diagnostics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Basil.Host.Logging;

/// <summary>Exports metrics and traces with OpenTelemetry when the configuration asks for it; nothing is exported otherwise.</summary>
public static class TelemetrySetup
{
	/// <summary>Adds the exporters the configuration switches on (the logs are exported by the logging setup).</summary>
	/// <param name="builder">The builder.</param>
	/// <returns><see langword="true" /> when something is exported.</returns>
	public static bool Add(WebApplicationBuilder builder)
	{
		var options = builder.Configuration.GetSection("Basil:Telemetry").Get<TelemetryOptions>() ?? new TelemetryOptions();
		builder.Services.AddOptions<TelemetryOptions>().Bind(builder.Configuration.GetSection("Basil:Telemetry"));
		if (!Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out var endpoint) || !(options.Metrics || options.Traces))
			return false;

		var telemetry = builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService("basil"));
		if (options.Metrics)
			telemetry.WithMetrics(metrics => metrics.AddMeter(BasilMeter.Instance.Name).AddAspNetCoreInstrumentation()
				.AddRuntimeInstrumentation().AddOtlpExporter(exporter => exporter.Endpoint = endpoint));
		if (options.Traces)
			telemetry.WithTracing(tracing => tracing.AddAspNetCoreInstrumentation()
				.AddOtlpExporter(exporter => exporter.Endpoint = endpoint));
		return true;
	}
}
