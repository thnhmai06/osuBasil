using Basil.Application.Content.Persistence;
using Basil.Application.Content.Ports;
using Basil.Application.Content.Storage;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Content.Adapters;
using Basil.Infrastructure.Content.Persistence;
using Basil.Infrastructure.Content.Storage;
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
		services.AddSingleton<EfMenuBannerRepository>();
		services.AddSingleton<IMenuBannerRepository>(provider =>
			provider.GetRequiredService<EfMenuBannerRepository>());
		services.AddSingleton<IMenuBannerStorage, FileMenuBannerStorage>();
		services.AddSingleton<IMenuIconStorage, FileMenuIconStorage>();
		services.AddSingleton<IMenuSeasonalsStorage, FileMenuSeasonalsStorage>();
		services.AddSingleton<IFaqStorage, FileFaqStorage>();
		services.AddSingleton<IScreenshotStorage, FileScreenshotStorage>();
		services.AddSingleton<IScreenshotInspector, ImageSharpScreenshotInspector>();
		return services;
	}
}