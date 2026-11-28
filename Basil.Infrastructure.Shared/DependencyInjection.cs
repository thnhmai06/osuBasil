using Basil.Application.Shared;
using Basil.Application.Shared.Events;
using Basil.Application.Shared.Replies;
using Basil.Application.Shared.Settings;
using Basil.Application.Shared.Storage;
using Basil.Infrastructure.Shared.Events;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Shared.Replies;
using Basil.Infrastructure.Shared.Registry;
using Basil.Infrastructure.Shared.Storage;
using Basil.Infrastructure.Shared.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
			services.TryAddSingleton<IPublishedEvents>(sp => sp.GetRequiredService<EventQueue>());
			services.TryAddSingleton(sp => new EventPublisher(
				new Lazy<IPublisher>(sp.GetRequiredService<IPublisher>),
				sp.GetRequiredService<TimeProvider>()));
			services.AddMemoryCache();
			services.AddSingleton<DataPaths>();
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

		/// <summary>Registers the wording of a surface: its reply enum is worded from <c>Data/Localizations/&lt;surface&gt;.json</c>.</summary>
		/// <param name="surface">The name of the surface, which is the name of its replies file.</param>
		public IServiceCollection AddReplies<TReply>(string surface) where TReply : struct, Enum
		{
			services.AddSingleton(sp => new ReplyCatalog<TReply>(surface, sp.GetRequiredService<DataPaths>(),
				sp.GetRequiredService<ILogger<ReplyCatalog<TReply>>>()));
			services.AddSingleton<IReplies<TReply>>(sp => sp.GetRequiredService<ReplyCatalog<TReply>>());
			services.AddSingleton<RepliesWatcher<TReply>>();
			services.AddHostedService(sp => sp.GetRequiredService<RepliesWatcher<TReply>>());
			return services;
		}

		public IServiceCollection AddSharedMigration()
		{
			services.AddSingleton<IModuleMigration, ModuleMigration<SharedDbContext>>();
			return services;
		}

		public IServiceCollection AddSharedInfrastructureOptions(IConfiguration configuration)
		{
			services.AddSingleton(Options.Create(DatabaseSetup.Read(configuration)));
			return services;
		}
	}

	private sealed class SharedInfrastructureRegistration;
}