using Basil.Application.Services.Content;
using Basil.Web.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global

namespace Basil.Web.Routing.Assets;

/// <summary>
///     Registers the `assets.` host's REST endpoints.
/// </summary>
/// <remarks>
///     The `assets.` host is Basil's internal CDN-equivalent: menu banner/icon/seasonal images,
///     beatmapset covers, and other media served through ImageSharp.Web instead of `api.`'s
///     hand-written file routes. The image requests themselves (`/menu/seasonals/{fileName}`,
///     `/menu/banners/{fileName}`, and — when it's an uploaded file — `/menu/icon`) are handled by
///     ImageSharp.Web providers before routing ever reaches this class; only the non-image requests
///     (listings, and the icon's external-URL/not-set fallback) are registered here.
/// </remarks>
internal static class AssetsHostRoutes
{
	/// <summary>
	///     Registers the `assets.{domain}` host's routes.
	/// </summary>
	/// <param name="group">The `assets.{domain}` route group.</param>
	public static void MapAssetsGroup(this RouteGroupBuilder group)
	{
		group.MapGet("/health", () => Results.Json(new HealthStatus("ok")))
			.WithGroupName("assets")
			.WithName("getAssetsHealth")
			.WithSummary("Get health.")
			.WithDescription("Returns `{ status: \"ok\" }` while the server is up.")
			.WithTags("Health")
			.Produces<HealthStatus>()
			.WithExample(StatusCodes.Status200OK, new HealthStatus("ok"));

		group.MapMenuAssetRoutes();
	}

	private sealed record HealthStatus(string Status);
}
