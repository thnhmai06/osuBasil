namespace Basil.Server.Features.Diagnostics;

/// <summary>Registers the Diagnostics slice's services.</summary>
public static class DiagnosticsServiceCollectionExtensions
{
	/// <summary>Registers the Diagnostics slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddDiagnostics(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<RuntimeMeterListener>();
		services.AddHostedService(sp => sp.GetRequiredService<RuntimeMeterListener>());

		services.AddSingleton<ProcessSampler>();
		services.AddSingleton<GcSampler>();

		return services;
	}
}
