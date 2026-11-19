using Basil.Application.Anticheat.Services;
using Basil.Infrastructure.Anticheat.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Anticheat;

public static class DependencyInjection
{
	public static IServiceCollection AddAnticheatInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IAnticheatReporter, AnticheatReporter>();
		return services;
	}
}