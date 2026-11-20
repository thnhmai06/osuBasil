using Basil.Application.Shared;
using Basil.Application.Shared.Settings;
using Basil.Application.Shared.Storage;
using Basil.Infrastructure.Shared.Events;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Shared.Registry;
using Basil.Infrastructure.Shared.Storage;
using Basil.Infrastructure.Shared.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mediator;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Shared;

public static class DependencyInjection
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddSharedInfrastructure()
		{
			if (services.Any(descriptor => descriptor.ServiceType == typeof(SharedInfrastructureRegistration)))
				return services;

			services.AddSingleton<SharedInfrastructureRegistration>();
			services.AddSharedApplication();
			services.TryAddSingleton(TimeProvider.System);
			services.TryAddSingleton<EventQueue>();
			services.TryAddSingleton(sp => new EventPublisher(
				new Lazy<IPublisher>(sp.GetRequiredService<IPublisher>),
				sp.GetRequiredService<TimeProvider>()));
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

		public IServiceCollection AddSharedMigration()
		{
			services.AddSingleton<IModuleMigration, ModuleMigration<SharedDbContext>>();
			return services;
		}

		public IServiceCollection AddSharedInfrastructureOptions(IConfiguration configuration)
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
	}

	private sealed class SharedInfrastructureRegistration;
}