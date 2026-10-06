using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Storage;

/// <summary>Registers the server's persistent storage: its data files and its database.</summary>
public static class DependencyInjection
{
	/// <summary>Adds the data paths, the database and the migrations applied at startup.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddInfrastructureStorage(this IServiceCollection services)
	{
		services.AddSingleton<DataPaths>();
		services.AddSingleton<Database>();
		services.AddSingleton<DatabaseMigrator>();
		services.AddHostedService<StorageStartup>();
		// Repositories and storages are registered by the phases that add them.
		return services;
	}
}
