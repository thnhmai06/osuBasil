using Basil.Application.Diagnostics.Services;
using Basil.Infrastructure.Diagnostics.Services;
using Basil.Infrastructure.Diagnostics.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Diagnostics;

public static class DependencyInjection
{
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