using Basil.Application.Common;
using Basil.Infrastructure.Common.Events;
using Basil.Infrastructure.Common.Files;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Basil.Infrastructure.Common;

/// <summary>Registers the infrastructure services shared by every module.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the shared database, file paths and event delivery services.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddCommonInfrastructure(this IServiceCollection services)
	{
		if (services.Any(descriptor => descriptor.ServiceType == typeof(CommonInfrastructureRegistration)))
			return services;

		services.AddSingleton<CommonInfrastructureRegistration>();
		services.AddCommonApplication();
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton<DataPaths>();
		services.AddSingleton<Database>();
		services.AddSingleton<DatabaseReader>();
		services.AddSingleton<DatabaseMigrator>();
		services.AddSingleton<IModuleMigration, ModuleMigration<CommonDbContext>>();
		services.AddSingleton<DatabaseWriter>();
		services.AddHostedService(provider => provider.GetRequiredService<DatabaseWriter>());
		services.AddHostedService<EventDispatcher>();
		services.AddHostedService<StorageStartup>();
		return services;
	}

	private sealed class CommonInfrastructureRegistration;
}