using Basil.Application.Servers.Ports;
using Basil.Infrastructure.Servers.Adapters;
using Basil.Infrastructure.Servers.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Servers;

public static class DependencyInjection
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddServersInfrastructure()
		{
			services.AddSingleton<IReleases, GitHubReleases>();
			return services;
		}

		public IServiceCollection AddServersInfrastructureOptions(IConfiguration configuration)
		{
			services.AddOptions<UpdateOptions>().Bind(configuration.GetSection("Basil:Updates"));
			return services;
		}

		public IServiceCollection AddServersBackgroundWork()
		{
			services.AddHostedService<UpdateCheckWorker>();
			return services;
		}
	}
}