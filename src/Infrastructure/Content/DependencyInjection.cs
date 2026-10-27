using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Postgres;
using Basil.Application.Content.Capabilities;
using Basil.Application.Content.Persistence;
using Basil.Infrastructure.Content.Capabilities;
using Basil.Infrastructure.Content.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Content;

/// <summary>Registers the persistence and capability implementations owned by Content.</summary>
public static class DependencyInjection
{
	/// <summary>Adds Content persistence and capability implementations.</summary>
	/// <param name="services">The service collection to add to.</param>
	/// <returns><paramref name="services" />, for chaining.</returns>
	public static IServiceCollection AddContentInfrastructure(this IServiceCollection services)
	{
		services.AddSingleton<IModuleMigration, ModuleMigration<ContentDbContext>>();
		services.AddSingleton<PostgresSettingsRepository>();
		services.AddSingleton<ISettingsRepository>(provider =>
			provider.GetRequiredService<PostgresSettingsRepository>());
		services.AddSingleton<IResident>(provider => provider.GetRequiredService<PostgresSettingsRepository>());
		services.AddSingleton<PostgresMenuBannerRepository>();
		services.AddSingleton<IMenuBannerRepository>(provider =>
			provider.GetRequiredService<PostgresMenuBannerRepository>());
		services.AddSingleton<IResident>(provider => provider.GetRequiredService<PostgresMenuBannerRepository>());
		services.AddSingleton<IMenuBannerStorage, FileMenuBannerStorage>();
		services.AddSingleton<IMenuIconStorage, FileMenuIconStorage>();
		services.AddSingleton<IMenuSeasonalsStorage, FileMenuSeasonalsStorage>();
		services.AddSingleton<IFaqStorage, FileFaqStorage>();
		services.AddSingleton<IScreenshotStorage, FileScreenshotStorage>();
		services.AddSingleton<IScreenshotInspector, ImageSharpScreenshotInspector>();
		return services;
	}
}