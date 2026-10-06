namespace Basil.Host.Api.Content;

/// <summary>Maps the Content slice's routes.</summary>
public static class ContentRouteMapping
{
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
}