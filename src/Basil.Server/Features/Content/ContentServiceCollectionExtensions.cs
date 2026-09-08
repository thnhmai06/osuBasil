using Basil.Server.Shared.Configuration;
using Basil.Server.Shared.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Basil.Server.Features.Content;

/// <summary>Registers the Content slice's services and endpoints.</summary>
public static class ContentServiceCollectionExtensions
{
	/// <summary>Registers the Content slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddContent(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<FaqService>();
		services.AddSingleton<MenuSeasonalService>();
		services.AddSingleton<MenuIconService>();
		services.AddSingleton<MenuBannerService>();
		services.AddSingleton<MotdService>();

		services.AddSingleton<ISettingsRepository>(sp =>
			new CachingSettingsRepository(
				new SqliteSettingsRepository(BuildConnectionString(sp)),
				sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<ILogger<CachingSettingsRepository>>()));
		services.AddSingleton<IMenuBannerRepository>(sp => new SqliteMenuBannerRepository(BuildConnectionString(sp)));

		return services;
	}

	/// <summary>Maps the Content slice's routes onto the `api.` host.</summary>
	/// <param name="group">The `api.{domain}` route group.</param>
	public static void MapContentRoutes(this RouteGroupBuilder group)
	{
		group.MapFaqRoutes();
		group.MapMenuSeasonalRoutes();
		group.MapMenuBannerRoutes();
		group.MapMenuIconRoutes();

		var settings = group.MapGroup("/settings");
		settings.MapMirrorSettingsRoutes();
		settings.MapMotdSettingsRoutes();

		group.MapAnnounceRoutes();
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}