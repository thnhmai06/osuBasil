using Basil.Application.Shared.Diagnostics;
using Basil.Application.Diagnostics.Services;
using Basil.Infrastructure.Diagnostics.Services;
using Basil.Infrastructure.Diagnostics.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Diagnostics;

/// <summary>Registers what the Diagnostics module implements.</summary>
public static class DependencyInjection
{
	/// <summary>
	///     Adds the intake, the feed and the sampler that feeds it; its handlers are registered with the other Mediator
	///     handlers.
	/// </summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddDiagnosticsInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton(sp => new RuntimeSampler(sp.GetRequiredService<TimeProvider>()));
		services.AddSingleton<IClearTally>(sp => sp.GetRequiredService<RuntimeSampler>());
		services.AddSingleton<ICacheCleaner, CacheCleaner>();
		services.AddSingleton<DiagnosticsFeed>();
		services.AddSingleton<IDiagnosticsFeed>(sp => sp.GetRequiredService<DiagnosticsFeed>());
		services.AddSingleton<IDiagnosticsIntake, DiagnosticsIntake>();
		services.AddHostedService<DiagnosticsSampler>();
		return services;
	}
}