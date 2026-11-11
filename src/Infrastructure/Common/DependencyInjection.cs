using Basil.Application.Common;
using Basil.Application.Common.Settings;
using Basil.Application.Common.Storage;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Common.Registry;
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
		services.AddMemoryCache();
		services.AddSingleton<DataPaths>();
		services.AddSingleton<IUploadSpool, FileUploadSpool>();
		services.AddSingleton<EfSettingsRepository>();
		services.AddSingleton<ISettingsRepository>(provider => provider.GetRequiredService<EfSettingsRepository>());
		services.AddSingleton<IResident>(provider => provider.GetRequiredService<EfSettingsRepository>());
		services.AddSingleton<Database>();
		services.AddSingleton<DatabaseSession>();
		services.AddSingleton<DatabaseMigrator>();
		services.AddHostedService<EventDispatcher>();
		services.AddHostedService<StorageStartup>();
		return services;
	}

	/// <summary>Adds the migration of the shared tables (the server settings).</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	/// <remarks>
	///     It must be called after the migrations of the module whose first migration created the <c>settings</c> table
	///     (Content), because the shared migration only creates that table when it is missing.
	/// </remarks>
	public static IServiceCollection AddCommonMigration(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<CommonDbContext>>();
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