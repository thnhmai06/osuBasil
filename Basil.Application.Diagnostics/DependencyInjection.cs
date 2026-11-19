using Basil.Application.Diagnostics.Objects;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Diagnostics;

public static class DependencyInjection
{
	public static IServiceCollection AddDiagnosticsApplication(this IServiceCollection services)
	{
		services.AddSingleton<ServerDiagnostics>();
		return services;
	}
}