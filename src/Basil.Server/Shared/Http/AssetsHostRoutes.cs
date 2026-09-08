using Basil.Server.Shared.Http.OpenApi;

// ReSharper disable ClassNeverInstantiated.Global

namespace Basil.Server.Shared.Http;

/// <summary>
///     Registers the `assets.` host's shared `/health` probe.
/// </summary>
/// <remarks>
///     The `assets.` host is Basil's internal CDN-equivalent: menu banner/icon/seasonal images,
///     beatmapset covers, and other media served through ImageSharp.Web instead of `api.`'s
///     handwritten file routes. The image requests themselves (`/menu/seasonals/{fileName}`,
///     `/menu/banners/{fileName}`, and — when it's an uploaded file — `/menu/icon`) are handled by
///     ImageSharp.Web provider before routing ever reaches this host's mapped endpoints. Every
///     slice-owned non-image request (listings, and the icon's external-URL/not-set fallback) is
///     mapped separately by <c>Host/SliceRegistration.MapAll</c>, not here.
/// </remarks>
internal static class AssetsHostRoutes
{
	/// <summary>
	///     Registers the `assets.{domain}` host's shared (non-slice-owned) routes.
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
	}

	private sealed record HealthStatus(string Status);
}