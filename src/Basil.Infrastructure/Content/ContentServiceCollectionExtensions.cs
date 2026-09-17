using Basil.Application.Content;
using Basil.Application.Shared.Configuration;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Content;

/// <summary>Registers the Content slice's services.</summary>
public static class ContentServiceCollectionExtensions
{
	/// <summary>Registers the Content slice's services into the given service collection.</summary>
	/// <param name="services">The service collection to add the registrations to.</param>
	/// <param name="configuration">The configuration whose option sections the registrations bind to.</param>
	/// <returns>The same service collection for chaining further registrations.</returns>
	public static IServiceCollection AddContent(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddContentApplication();

		services.AddSingleton<FaqService>();
		services.AddSingleton<IFaqStore>(sp => sp.GetRequiredService<FaqService>());
		services.AddSingleton<MenuSeasonalService>();
		services.AddSingleton<MenuIconService>();
		services.AddSingleton<MenuBannerService>();

		services.AddSingleton<ISettingsRepository>(sp =>
			new CachingSettingsRepository(
				new SqliteSettingsRepository(BuildConnectionString(sp)),
				sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<ILogger<CachingSettingsRepository>>()));
		services.AddSingleton<IMenuBannerRepository>(sp => new SqliteMenuBannerRepository(BuildConnectionString(sp)));

		return services;
	}

	private static string BuildConnectionString(IServiceProvider sp)
	{
		return sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Build();
	}
}