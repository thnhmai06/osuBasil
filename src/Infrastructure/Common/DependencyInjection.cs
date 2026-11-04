using Basil.Application.Common;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Common.Storage;
using Basil.Infrastructure.Common.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

	/// <summary>
	///     Reads the storage location (<c>Basil:Storage</c>) and the database settings (<c>ConnectionStrings</c>,
	///     <c>Basil:Database</c>) once, so a later edit of the files waits for the next start.
	/// </summary>
	/// <param name="services">The service collection to add to.</param>
	/// <param name="configuration">The configuration to read.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	/// <exception cref="InvalidOperationException">There is no storage location or no connection string to store changes.</exception>
	public static IServiceCollection AddCommonInfrastructureOptions(this IServiceCollection services,
		IConfiguration configuration)
	{
		var storage = configuration.GetSection("Basil:Storage").Get<StorageOptions>();
		if (string.IsNullOrWhiteSpace(storage?.Location))
			throw new InvalidOperationException("Basil:Storage:Location is not set.");
		// A relative location is relative to the executable.
		if (!Path.IsPathRooted(storage.Location))
			storage.Location = Path.Combine(AppContext.BaseDirectory, storage.Location);
		services.AddSingleton(Options.Create(storage));
		services.AddSingleton(Options.Create(DatabaseSetup.Read(configuration)));
		return services;
	}

	private sealed class CommonInfrastructureRegistration;
}