using Basil.Application.Diagnostics.Objects;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Diagnostics;

/// <summary>Registers the runtime objects owned by Diagnostics.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the Diagnostics runtime objects.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddDiagnosticsApplication(this IServiceCollection services)
	{
		services.AddSingleton<ServerDiagnostics>();
		return services;
	}
}